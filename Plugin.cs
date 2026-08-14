using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace nobugs;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    internal static ConfigEntry<bool> RemoveSpiders;
    internal static ConfigEntry<bool> RemoveScorpions;
    internal static ConfigEntry<bool> RemoveBeetles;
    internal static ConfigEntry<bool> RemoveBees;
    internal static ConfigEntry<bool> RemoveAntlions;
    internal static ConfigEntry<bool> RemoveFrogTongues;
    internal static ConfigEntry<bool> DestroyObjects;

    private Harmony _harmony;

    private void Awake()
    {
        Logger = base.Logger;

        RemoveSpiders = Config.Bind("Bugs", "RemoveSpiders", true,
            "移除蜘蛛（Spider）：不再从天花板垂降抓人。");
        RemoveScorpions = Config.Bind("Bugs", "RemoveScorpions", true,
            "移除蝎子（Scorpion）：不再造成中毒和击退。");
        RemoveBeetles = Config.Bind("Bugs", "RemoveBeetles", true,
            "移除甲虫（Beetle）：不再顶飞玩家。");
        RemoveBees = Config.Bind("Bugs", "RemoveBees", true,
            "移除蜂群（BeeSwarm）：蜂巢不再孵化蜂群。");
        RemoveAntlions = Config.Bind("Bugs", "RemoveAntlions", true,
            "移除蚁狮（Antlion）：沙坑不再吞噬玩家。");
        RemoveFrogTongues = Config.Bind("Bugs", "RemoveFrogTongues", false,
            "移除青蛙舌（FrogTongue）。青蛙不算昆虫，默认关闭。");
        DestroyObjects = Config.Bind("General", "DestroyObjects", true,
            "true = 直接销毁虫子对象（更彻底）；false = 仅隐藏并禁用其行为（联机更安全）。");

        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        _harmony.PatchAll(typeof(BugPatches));

        Logger.LogInfo($"{MyPluginInfo.PLUGIN_NAME} v{MyPluginInfo.PLUGIN_VERSION} 已加载，昆虫清理已启用。");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }

    /// <summary>
    /// 统一的移除逻辑：根据配置销毁或隐藏目标对象。
    /// </summary>
    internal static void Remove(Behaviour target, string label)
    {
        if (target == null)
        {
            return;
        }

        GameObject go = target.gameObject;

        if (DestroyObjects.Value)
        {
            Logger.LogInfo($"[NoBugs] 销毁 {label}: {go.name}");
            UnityEngine.Object.Destroy(go);
        }
        else
        {
            Logger.LogInfo($"[NoBugs] 禁用 {label}: {go.name}");
            target.enabled = false;
            go.SetActive(false);
        }
    }
}

[HarmonyPatch]
internal static class BugPatches
{
    // ---------- 蜘蛛 ----------
    // Spider 是场景中手工摆放的预制体，Start() 里会向 SpiderManager 注册自己。
    // 在 Start 之前返回 false，可阻止注册，蜘蛛便永远不会执行 Scan() 抓人。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Spider), "Start")]
    private static bool Spider_Start_Prefix(Spider __instance)
    {
        if (!Plugin.RemoveSpiders.Value)
        {
            return true;
        }

        Plugin.Remove(__instance, "Spider");
        return false;
    }

    // 双保险：即使因加载顺序漏过了 Start，Scan() 也不会执行抓人检测。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Spider), "Scan")]
    private static bool Spider_Scan_Prefix()
    {
        return !Plugin.RemoveSpiders.Value;
    }

    // 蜘蛛触发器：彻底阻断抓人入口。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(SpiderTrigger), "OnTriggerEnter")]
    private static bool SpiderTrigger_OnTriggerEnter_Prefix()
    {
        return !Plugin.RemoveSpiders.Value;
    }

    // ---------- 蝎子 / 甲虫（Mob 派生） ----------
    // Scorpion 与 Beetle 都继承 Mob，Mob.Start() 会调用 MobManager.Register()。
    // 这里在 Mob.Start() 之后按实际类型处理，避免影响其他非昆虫 Mob。
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Mob), "Start")]
    private static void Mob_Start_Postfix(Mob __instance)
    {
        if (__instance is Scorpion && Plugin.RemoveScorpions.Value)
        {
            Plugin.Remove(__instance, "Scorpion");
            return;
        }

        if (__instance is Beetle && Plugin.RemoveBeetles.Value)
        {
            Plugin.Remove(__instance, "Beetle");
            return;
        }

        if (__instance is FrogTongue && Plugin.RemoveFrogTongues.Value)
        {
            Plugin.Remove(__instance, "FrogTongue");
        }
    }

    // 双保险：万一虫子对象仍存活，也不允许其造成伤害。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Scorpion), "InflictAttack")]
    private static bool Scorpion_InflictAttack_Prefix()
    {
        return !Plugin.RemoveScorpions.Value;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Beetle), "InflictAttack")]
    private static bool Beetle_InflictAttack_Prefix()
    {
        return !Plugin.RemoveBeetles.Value;
    }

    // ---------- 蜂群 ----------
    // Beehive.Init() 内通过 PhotonNetwork.Instantiate 生成 BeeSwarm。
    // 把 spawnBees 置为 false，可从源头阻止联网生成蜂群（避免产生网络对象）。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Beehive), "Init")]
    private static void Beehive_Init_Prefix(Beehive __instance)
    {
        if (Plugin.RemoveBees.Value)
        {
            __instance.spawnBees = false;
        }
    }

    // 已经存在于场景/存档中的蜂群，在其 Start() 时移除。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(BeeSwarm), "Start")]
    private static bool BeeSwarm_Start_Prefix(BeeSwarm __instance)
    {
        if (!Plugin.RemoveBees.Value)
        {
            return true;
        }

        Plugin.Remove(__instance, "BeeSwarm");
        return false;
    }

    // ---------- 蚁狮 ----------
    // Antlion.Start() 自身就有 Hazard_Antlion 开关判断，这里沿用游戏的禁用方式。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Antlion), "Start")]
    private static bool Antlion_Start_Prefix(Antlion __instance)
    {
        if (!Plugin.RemoveAntlions.Value)
        {
            return true;
        }

        Plugin.Remove(__instance, "Antlion");
        return false;
    }
}
