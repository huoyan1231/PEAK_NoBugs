# PEAK NoBugs

一个用于游戏 **PEAK** 的 [BepInEx 5](https://github.com/BepInEx/BepInEx) 插件，移除关卡中所有的昆虫（蜘蛛、蝎子、甲虫、蜂群、蚁狮），让有昆虫恐惧症的玩家可以安心游玩。

> 只针对"昆虫"生效。僵尸、渡鸦、雪人、水母等非昆虫的环境 Hazard 不在本插件范围内（游戏本身已自带对应开关）。

## 支持的昆虫

| 昆虫 | 默认 | 说明 |
|---|---|---|
| 蜘蛛 Spider | 移除 | 不再从天花板垂降并抓住玩家 |
| 蝎子 Scorpion | 移除 | 不再中毒 / 击退 |
| 甲虫 Beetle | 移除 | 不再把玩家顶飞 |
| 蜂群 BeeSwarm | 移除 | 蜂巢不再孵化蜂群（从源头阻止生成） |
| 蚁狮 Antlion | 移除 | 沙坑不再吞噬玩家 |
| 青蛙舌 FrogTongue | 保留 | 两栖类，不算昆虫，默认不移除 |

每种昆虫都能在配置文件里单独开关。

## 安装

前置条件：游戏目录已安装 **BepInEx 5**（本项目基于 `5.4.23` 编译）。

1. 编译本项目（见下方"构建"），得到 `bin/Debug/netstandard2.1/nobugs.dll`。
2. 将 `nobugs.dll` 复制到游戏的插件目录：

   ```
   <Steam>/steamapps/common/PEAK/BepInEx/plugins/nobugs.dll
   ```

3. 启动游戏，日志中应出现：

   ```
   [Info  :   nobugs] PEAK NoBugs v1.0.0 已加载，昆虫清理已启用。
   ```

## 配置

配置文件在 `<PEAK>/BepInEx/config/nobugs.cfg`，可随时修改后重启游戏生效：

```ini
[Bugs]
# 是否移除蜘蛛
RemoveSpiders = true
# 是否移除蝎子
RemoveScorpions = true
# 是否移除甲虫
RemoveBeetles = true
# 是否移除蜂群
RemoveBees = true
# 是否移除蚁狮
RemoveAntlions = true
# 是否移除青蛙舌（非昆虫，默认关闭）
RemoveFrogTongues = false

[General]
# true = 直接销毁虫子对象（更彻底）；false = 仅隐藏并禁用其行为（联机更安全）
DestroyObjects = true
```

## 实现原理

通过 [Harmony](https://github.com/BepInEx/HarmonyX) 在昆虫各自的生命周期入口打补丁。具体针对游戏的反编译分析（见 `agent.md`）：

- `Spider.Start` / `Spider.Scan` / `SpiderTrigger.OnTriggerEnter`
  —— 阻止蜘蛛向 `SpiderManager` 注册并阻断垂降抓人；
- `Mob.Start`（Postfix，运行时按类型判断） —— 移除 `Scorpion` / `Beetle` / `FrogTongue`（三者都继承 `Mob`，但并未重写 `Start`）；
- `Scorpion.InflictAttack` / `Beetle.InflictAttack` —— 双保险，即使对象残留也不会造成伤害；
- `Beehive.Init` —— 将 `spawnBees` 置为 `false`，从源头阻止联网生成蜂群（比事后销毁更干净，避免多余 Photon 网络对象）；
- `BeeSwarm.Start` —— 移除已存在于场景 / 存档中的蜂群；
- `Antlion.Start` —— 移除蚁狮。

每个目标都采用"阻止生成 + 阻断伤害"的双重保险，规避加载顺序差异。

### 为什么不能只改游戏自带的 Hazard 开关

`RunSettings` 里虽然定义了 `Hazard_Spiders` / `Hazard_Beetles` / `Hazard_Scorpions` / `Hazard_Bees`，但经逆向分析确认，这些枚举值除了注册默认值以外，**从未被任何昆虫代码读取**；只有 `Antlion` 和 `FrogTongue` 真正自检了开关。游戏自带的 "Bug Phobia"（昆虫恐惧症）模式也只是把虫子模型换成其他造型，行为伤害依旧存在。因此必须用补丁才能真正移除。

## 联机注意事项

虫子的生成与状态由房主（MasterClient）主导：

- 若**仅客户端**安装本插件，本地虫子会被销毁，但可能与房主状态不一致；
- 建议**房主也安装**，或把 `DestroyObjects` 设为 `false`（改为仅隐藏并禁用），以降低联机不同步的风险。

## 构建

需要 .NET SDK 与游戏程序集（位于项目 `lib/` 目录，已从游戏安装目录 `PEAK_Data/Managed` 提取）：

```powershell
dotnet build
```

输出：`bin/Debug/netstandard2.1/nobugs.dll`

> 编译期依赖的 `BepInEx.dll` / `0Harmony.dll` / `Photon*.dll` / `Assembly-CSharp.dll` 均来自 `lib/`，以保证与游戏运行时版本一致。
> `lib/`、`dnSpy/`、`obj/`、`bin/`、`decompiled/` 已在 `.gitignore` 中排除，不会进入仓库。

## 目录说明

| 路径 | 说明 |
|---|---|
| `Plugin.cs` | 插件全部实现（入口 + Harmony 补丁） |
| `nobugs.csproj` | 项目文件，目标框架 `netstandard2.1` |
| `lib/` | 编译引用用的游戏 / BepInEx 程序集（不提交） |
| `dnSpy/` | dnSpy 反编译工具 |
| `decompiled/` | dnSpy 反编译出的参考源码（不提交，不参与编译） |
| `agent.md` | 逆向分析结论与实现细节（供开发者参考） |
