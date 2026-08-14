# PEAK_NoBugs — 项目说明（agent.md）

BepInEx 5 插件：移除游戏 **PEAK** 中的所有昆虫（蜘蛛、蝎子、甲虫、蜂群、蚁狮）。

## 当前状态

已完成并通过编译（`dotnet build` → 已成功生成）。
产物：`bin/Debug/netstandard2.1/nobugs.dll`

## 目录结构

| 路径 | 说明 |
|---|---|
| `Plugin.cs` | 插件全部实现（入口 + 所有 Harmony 补丁） |
| `nobugs.csproj` | 项目文件，目标框架 `netstandard2.1` |
| `lib/` | 编译引用用的游戏程序集（不参与分发） |
| `dnSpy/` | dnSpy 反编译工具（含 `dnSpy.Console.exe`） |
| `decompiled/` | dnSpy 反编译出的参考源码，**已在 csproj 中排除编译** |

`lib/` 内程序集来源：`E:\SteamLibrary\steamapps\common\PEAK\PEAK_Data\Managed`
- `Assembly-CSharp.dll`（游戏逻辑，提供 Spider / Scorpion / Beetle / Beehive 等类型）
- `PhotonUnityNetworking.dll`、`PhotonRealtime.dll`（`Mob` 继承 `MonoBehaviourPunCallbacks`，必须引用）

## 反编译命令

```powershell
.\dnSpy\dnSpy.Console.exe -o .\decompiled --no-sln --no-resources --no-resx .\lib\Assembly-CSharp.dll
```

注意：`dnSpy.Console.exe` 不支持 `--silence` 参数（skill 文档有误）。

## 逆向分析结论（关键）

### 1. 昆虫类型清单

| 生物 | 类 | 基类 | 生成方式 |
|---|---|---|---|
| 蜘蛛 | `Spider` | `MonoBehaviour` | 场景中手工摆放的预制体 |
| 蝎子 | `Scorpion` | `Mob` | 场景预制体 |
| 甲虫 | `Beetle` | `Mob` | 场景预制体 |
| 蜂群 | `BeeSwarm` | `MonoBehaviourPun` | `Beehive.Init()` 内 `PhotonNetwork.Instantiate` 动态生成 |
| 蚁狮 | `Antlion` | `MonoBehaviour` | 场景预制体 |
| 青蛙舌 | `FrogTongue` | `Mob` | 场景预制体（非昆虫，默认不移除） |

`Mob` 的派生类**只有** `Scorpion`、`Beetle`、`FrogTongue` 三个。

### 2. 没有集中式生成入口

游戏**不存在** `SpawnMob()` 之类的统一生成方法。除蜂群外，所有虫子都是作为预制体静态摆放在关卡中的，靠自身 `Start()` 向全局管理器注册：
- `Mob.Start()` → `MobManager.instance.Register(this)`
- `Spider.Start()` → `SpiderManager.instance.Register(this)`

因此**补丁点选在各自的 `Start()`**，而不是某个 spawner。

### 3. 游戏自带的 Hazard 开关不可靠（重要坑点）

`RunSettings.SETTINGTYPE` 中定义了 `Hazard_Spiders` / `Hazard_Beetles` / `Hazard_Scorpions` / `Hazard_Bees`，但经全代码库搜索确认：**这些枚举值除了在 `RunSettings.cs` 里注册默认值以外，从未被任何生物代码读取**。

只有 `Antlion.Start()`（`Antlion.cs:13`）和 `FrogTongue`（`FrogTongue.cs:261,277`）真正检查了自己的 Hazard 开关。

结论：**单纯修改 RunSettings 无法移除蜘蛛/蝎子/甲虫/蜂群**，必须用 Harmony 补丁。

### 4. `BugPhobia` 只是换皮，不是移除

`BugPhobia.Start()` 读取 `BugPhobiaSetting`，在 `defaultGameObjects` 与 `bugPhobiaGameObjects` 之间切换显示（把虫子换成 "Bing Bong" 造型），虫子行为与伤害依旧存在。因此不能复用该机制来实现移除。

## 实现方案

`Plugin.cs` 中 `BugPatches` 类共 9 个补丁，均已核验目标方法签名存在：

| 补丁目标 | 类型 | 作用 |
|---|---|---|
| `Spider.Start()` | Prefix | 阻止注册并移除蜘蛛 |
| `Spider.Scan()` | Prefix | 双保险，阻断垂降抓人检测 |
| `SpiderTrigger.OnTriggerEnter()` | Prefix | 双保险，阻断抓人触发 |
| `Mob.Start()` | Postfix | 按运行时类型移除 Scorpion / Beetle / FrogTongue |
| `Scorpion.InflictAttack()` | Prefix | 双保险，阻断中毒伤害 |
| `Beetle.InflictAttack()` | Prefix | 双保险，阻断顶飞 |
| `Beehive.Init()` | Prefix | 把 `spawnBees` 置 false，从源头阻止联网生成蜂群 |
| `BeeSwarm.Start()` | Prefix | 移除存档/场景中已存在的蜂群 |
| `Antlion.Start()` | Prefix | 移除蚁狮 |

设计要点：
- 在 `Mob.Start()` 上用 **Postfix + 运行时类型判断**，而不是分别 patch 子类的 `Start`（子类并未重写 `Start`），同时避免影响未来新增的非昆虫 Mob。
- 蜂群走 `Beehive.Init()` 把 `spawnBees` 置 false，避免产生多余的 Photon 网络对象，比事后销毁更干净。
- 每类虫子都配了"阻止生成"+"阻断伤害"双重保险，规避加载顺序问题。
- `Plugin.Remove()` 参数类型是 `Behaviour`（不是 `Component`），因为需要访问 `.enabled`。

## 配置项

配置文件：`BepInEx/config/nobugs.cfg`

| 分区 | 键 | 默认 | 说明 |
|---|---|---|---|
| `Bugs` | `RemoveSpiders` | `true` | 移除蜘蛛 |
| `Bugs` | `RemoveScorpions` | `true` | 移除蝎子 |
| `Bugs` | `RemoveBeetles` | `true` | 移除甲虫 |
| `Bugs` | `RemoveBees` | `true` | 移除蜂群 |
| `Bugs` | `RemoveAntlions` | `true` | 移除蚁狮 |
| `Bugs` | `RemoveFrogTongues` | `false` | 移除青蛙舌（非昆虫，默认关） |
| `General` | `DestroyObjects` | `true` | `true` 销毁对象；`false` 仅隐藏禁用（联机更安全） |

## 构建与安装

```powershell
dotnet build
# 产物 bin/Debug/netstandard2.1/nobugs.dll 复制到：
# E:\SteamLibrary\steamapps\common\PEAK\BepInEx\plugins\
```

前提：游戏目录需先安装 BepInEx 5（当前游戏目录尚未安装，`BepInEx/` 不存在）。

## 已知注意事项

- **联机**：虫子的生成/状态由主机（MasterClient）主导。若仅客户端装插件，本地虫子被销毁可能导致与主机状态不一致；建议**房主也装**，或把 `DestroyObjects` 设为 `false` 以降低风险。
- 编译时会有一批 `MSB3277` 版本冲突警告，来源是 `dnSpy/bin` 下的 `Newtonsoft.Json.dll` 被探测到，不影响产物。
- 首次构建若报 mscorlib/System.String 未定义，先 `Remove-Item -Recurse -Force obj,bin` 再 `dotnet restore --force`。
