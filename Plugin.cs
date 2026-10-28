using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TheShack.ClassicSnap
{
    // Valheim 1.0 lets you pick which snap point of the piece you are placing is used: in build mode, TabLeft/TabRight
    // (Q/E by default) step through Auto -> Top -> Bottom -> Edge ... Extra Snap Points Made Easy (ESP) switches that
    // off by default, and its extra snap points reuse the same names, so even with vanilla cycling enabled the list
    // becomes long and the old Top/Bottom/Edge choice is lost.
    //
    // This plugin adds a "Classic" mode to ESP's Toggle Manual Snap Mode key (CapsLock in The Shack Pack), which now
    // cycles Classic -> Auto -> Manual -> Classic. In Classic:
    //   - ESP's extra snap points are hidden, on the piece being placed and on the pieces it snaps to, so only the
    //     game's original points are used;
    //   - vanilla manual snapping works again (Q/E picks the snap point, as in the base game);
    //   - ESP itself sits in Auto mode.
    // ESP's other keys (Manual+, Grid) still work from Classic and leave it. The mode is remembered between
    // sessions; a fresh install starts in Classic.
    //
    // ESP appends its snap points as the last children of each piece prefab, after the piece's own children, and keeps
    // the list it added (SnapPointManager.AddedSnapPoints). The sibling index of the first one per prefab is therefore
    // the boundary between original and extra points, on the prefab and on every instance and placement ghost made
    // from it. Piece.GetSnapPoints is the only place Valheim lists snap points, so filtering there covers both sides.
    //
    // ESP is reached only through reflection; if an ESP update renames something this plugin logs a warning and does
    // nothing, rather than fail to load.
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(EspGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "theshack.classicsnap";
        public const string PluginName = "Classic Snap";
        public const string PluginVersion = "1.0.0";

        private const string EspGuid = "Searica.Valheim.ExtraSnapPointsMadeEasy";
        private const string ClassicName = "Classic";

        private static ManualLogSource log;
        private static ConfigEntry<bool> classicSnap;

        // ESP internals
        private static object espPlugin;                 // ExtraSnapsPlugin.Instance
        private static PropertyInfo espToggleManualKey;  // ExtraSnapsPlugin.ToggleManualSnap : ConfigEntry<KeyCode>
        private static PropertyInfo espNotificationType; // ExtraSnapsPlugin.NotificationType : ConfigEntry<MessageHud.MessageType>
        private static FieldInfo espModeField;           // SnapModeManager.CurrentSnapMode
        private static object espModeAuto;
        private static object espModeManual;
        private static MethodInfo espModeChanged;        // SnapModeManager.InvokeOnSnapModeChanged (updates ESP's key hints)
        private static object espSnapPointManager;       // SnapPointManager.Instance
        private static FieldInfo espAddedSnapPoints;     // SnapPointManager.AddedSnapPoints : List<GameObject>

        private static readonly AccessTools.FieldRef<Player, int> manualSnapPoint =
            AccessTools.FieldRefAccess<Player, int>("m_manualSnapPoint");

        // Prefab name -> sibling index of the first ESP snap point. Entries are only ever added or lowered: the
        // number of original children of a prefab never changes, and pieces spawned before an ESP re-init still
        // carry the old extra points at the same positions.
        private static readonly Dictionary<string, int> firstExtraIndex = new Dictionary<string, int>();
        private static readonly Dictionary<int, int> firstExtraIndexByInstance = new Dictionary<int, int>();
        private static bool indexDirty = true;

        private void Awake()
        {
            log = Logger;
            classicSnap = Config.Bind("General", "Classic", true,
                "Whether Classic mode is on: only the game's original snap points, and Q/E picks the snap point as in " +
                "the base game. Switched in game with Extra Snap Points' Toggle Manual Snap Mode key, which cycles " +
                "Classic -> Auto -> Manual. Remembered between sessions.");

            try
            {
                if (!Resolve())
                {
                    log.LogWarning("Extra Snap Points Made Easy internals not found (renamed in an update?). Classic Snap is off.");
                    return;
                }
                Harmony harmony = new Harmony(PluginGuid);
                harmony.PatchAll(typeof(Plugin).Assembly);
                PatchEsp(harmony);
                log.LogInfo($"ready, {(classicSnap.Value ? ClassicName : "ESP snapping")} at start");
            }
            catch (Exception e)
            {
                log.LogWarning("could not hook Extra Snap Points Made Easy, Classic Snap is off: " + e);
            }
        }

        private static bool Resolve()
        {
            Type plugin = AccessTools.TypeByName("ExtraSnapsMadeEasy.ExtraSnapsPlugin");
            Type modeManager = AccessTools.TypeByName("ExtraSnapsMadeEasy.Patches.SnapModeManager");
            Type snapPointManager = AccessTools.TypeByName("ExtraSnapsMadeEasy.ExtraSnapPoints.SnapPointManager");
            if (plugin == null || modeManager == null || snapPointManager == null)
            {
                return false;
            }
            espPlugin = AccessTools.Field(plugin, "Instance")?.GetValue(null);
            espToggleManualKey = AccessTools.Property(plugin, "ToggleManualSnap");
            espNotificationType = AccessTools.Property(plugin, "NotificationType");
            espModeField = AccessTools.Field(modeManager, "CurrentSnapMode");
            espModeChanged = AccessTools.Method(modeManager, "InvokeOnSnapModeChanged");
            espSnapPointManager = AccessTools.Field(snapPointManager, "Instance")?.GetValue(null);
            espAddedSnapPoints = AccessTools.Field(snapPointManager, "AddedSnapPoints");
            if (espPlugin == null || espToggleManualKey == null || espNotificationType == null || espModeField == null ||
                espSnapPointManager == null || espAddedSnapPoints == null)
            {
                return false;
            }
            espModeAuto = Enum.Parse(espModeField.FieldType, "Auto");
            espModeManual = Enum.Parse(espModeField.FieldType, "Manual");
            return true;
        }

        private static void PatchEsp(Harmony harmony)
        {
            Type modeManager = espModeField.DeclaringType;
            Type disableVanilla = AccessTools.TypeByName("ExtraSnapsMadeEasy.Patches.DisableVanillaManualSnapPatches");
            Type adder = AccessTools.TypeByName("ExtraSnapsMadeEasy.ExtraSnapPoints.ExtraSnapsAdder");

            Patch(harmony, AccessTools.Method(modeManager, "UpdatePlacementGhostPostfix"),
                nameof(ModeKey_Prefix), nameof(ModeKey_Postfix));
            // ESP resets the vanilla snap point to Auto every frame, once in a prefix and once mid-method.
            Patch(harmony, AccessTools.Method(disableVanilla, "UpdatePlacementGhost_Postfix"), nameof(SkipInClassic), null);
            Patch(harmony, AccessTools.Method(disableVanilla, "ResetManualSnapPointToAuto"), nameof(SkipInClassic), null);
            Patch(harmony, AccessTools.Method(adder, "AddExtraSnapPoints"), null, nameof(MarkIndexDirty));
        }

        private static void Patch(Harmony harmony, MethodInfo target, string prefix, string postfix)
        {
            if (target == null)
            {
                throw new MissingMethodException("an Extra Snap Points Made Easy method for " + (prefix ?? postfix));
            }
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(Plugin), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(Plugin), postfix));
        }

        // ---------------------------------------------------------------- mode key

        // Runs before ESP reads its mode keys. Handles the two presses of the Manual key that now lead into and out
        // of Classic, and lets ESP handle everything else (Auto/Manual+/Grid -> Manual, and its other keys).
        // ESP's method is itself a Harmony patch whose parameter is named __instance; Harmony would read that name
        // here as the (null) instance of a static method, so the player is taken by position.
        private static bool ModeKey_Prefix(Player __0)
        {
            Player player = __0;
            if (player == null || player != Player.m_localPlayer || !player.InPlaceMode() || player.IsDead())
            {
                return true;
            }
            KeyCode key = ((ConfigEntry<KeyCode>)espToggleManualKey.GetValue(espPlugin)).Value;
            if (!Input.GetKeyDown(key))
            {
                return true;
            }
            if (classicSnap.Value)
            {
                SetClassic(false, player);
                SetEspMode(espModeAuto);
                Notify(player, "Snap Mode: Auto");
                return false; // ESP would otherwise read the same key press and switch on to Manual
            }
            if (espModeField.GetValue(null).Equals(espModeManual))
            {
                SetEspMode(espModeAuto);
                SetClassic(true, player);
                Notify(player, "Snap Mode: " + ClassicName);
                return false;
            }
            return true;
        }

        // ESP's Manual+ and Grid keys still work in Classic; switching to either leaves it.
        private static void ModeKey_Postfix(Player __0)
        {
            if (classicSnap.Value && !espModeField.GetValue(null).Equals(espModeAuto))
            {
                SetClassic(false, __0);
            }
        }

        private static bool SkipInClassic() => !classicSnap.Value;

        private static void SetClassic(bool on, Player player)
        {
            if (classicSnap.Value != on)
            {
                log.LogInfo(on ? "switched to Classic" : "left Classic");
            }
            classicSnap.Value = on;
            if (player != null)
            {
                manualSnapPoint(player) = -1;
            }
        }

        private static void SetEspMode(object mode)
        {
            espModeField.SetValue(null, mode);
            espModeChanged?.Invoke(null, null);
        }

        private static void Notify(Player player, string text)
        {
            object type = ((ConfigEntryBase)espNotificationType.GetValue(espPlugin)).BoxedValue;
            player.Message((MessageHud.MessageType)type, text);
        }

        // ---------------------------------------------------------------- snap point filter

        private static void MarkIndexDirty() => indexDirty = true;

        [HarmonyPatch(typeof(Piece), nameof(Piece.GetSnapPoints), new[] { typeof(List<Transform>) })]
        private static class PieceGetSnapPointsPatch
        {
            private static void Prefix(List<Transform> points, out int __state)
            {
                __state = points?.Count ?? 0;
            }

            // Only the points this call added are looked at: the static Piece.GetSnapPoints gathers several pieces
            // into one list.
            private static void Postfix(Piece __instance, List<Transform> points, int __state)
            {
                if (!classicSnap.Value || points == null || points.Count <= __state)
                {
                    return;
                }
                int first = FirstExtraIndex(__instance);
                if (first < 0)
                {
                    return;
                }
                Transform root = __instance.transform;
                for (int i = points.Count - 1; i >= __state; i--)
                {
                    Transform point = points[i];
                    if (point != null && point.parent == root && point.GetSiblingIndex() >= first)
                    {
                        points.RemoveAt(i);
                    }
                }
            }
        }

        private static int FirstExtraIndex(Piece piece)
        {
            if (indexDirty)
            {
                RebuildIndex();
            }
            int id = piece.GetInstanceID();
            if (firstExtraIndexByInstance.TryGetValue(id, out int first))
            {
                return first;
            }
            if (!firstExtraIndex.TryGetValue(Utils.GetPrefabName(piece.gameObject), out first))
            {
                first = -1;
            }
            if (firstExtraIndexByInstance.Count > 100000)
            {
                firstExtraIndexByInstance.Clear();
            }
            firstExtraIndexByInstance[id] = first;
            return first;
        }

        private static void RebuildIndex()
        {
            indexDirty = false;
            if (!(espAddedSnapPoints.GetValue(espSnapPointManager) is List<GameObject> added))
            {
                return;
            }
            foreach (GameObject point in added)
            {
                Transform prefab = point == null ? null : point.transform.parent;
                if (prefab == null)
                {
                    continue;
                }
                int index = point.transform.GetSiblingIndex();
                if (!firstExtraIndex.TryGetValue(prefab.name, out int current) || index < current)
                {
                    firstExtraIndex[prefab.name] = index;
                }
            }
            firstExtraIndexByInstance.Clear();
            log.LogInfo($"indexed extra snap points on {firstExtraIndex.Count} piece prefabs");
        }
    }
}
