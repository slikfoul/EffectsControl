using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace EffectsControl
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [DefaultExecutionOrder(10000)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "Slikfoul.EffectsControl";
        public const string PluginName = "EffectsControl";
        public const string PluginVersion = "1.1.0";
        internal static Plugin Instance;
        internal readonly EffectCatalog Catalog = new EffectCatalog();
        private readonly Dictionary<GameObject, VisualState> active = new Dictionary<GameObject, VisualState>();
        private readonly Dictionary<GameObject, EffectStage> pending = new Dictionary<GameObject, EffectStage>();
        private readonly Dictionary<GameObject, EffectStage> inactiveShields = new Dictionary<GameObject, EffectStage>();
        private readonly HashSet<GameObject> pendingNetwork = new HashSet<GameObject>();
        private readonly List<GameObject> expired = new List<GameObject>();
        private readonly VisualOptions[] options = new VisualOptions[(int)EffectStage.NorthernVengeance + 1];
        private Settings settings;
        private Harmony harmony;
        private bool rebuild = true, ready, shieldConflict;
        private bool optionsDirty = true;
        private int externalShieldMask;
        private int previousShieldMask;
        private bool rescanShields;
        private BaseUnityPlugin prioritySource;
        private Func<int> readExternalShieldMask;
        private int failures;

        private void Awake()
        {
            Instance = this;
            settings = new Settings(Config);
            Settings.BindOwnerNotices(Config, stage => (externalShieldMask & stage.ShieldBit()) != 0);
            settings.Changed += () => { optionsDirty = true; };
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Logger.LogInfo("Client visual mod: headless host skipped."); return; }
            harmony = new Harmony(PluginGuid);
            try { harmony.PatchAll(typeof(Plugin).Assembly); ready = true; }
            catch (Exception error) { harmony.UnpatchSelf(); Logger.LogError("EffectsControl patches failed; original visuals retained. " + error); }
            Logger.LogInfo("EffectsControl " + PluginVersion + ": weapon and magic shield visual controls ready.");
        }

        internal void RequestCatalog() { rebuild = true; }
        private void RebuildCatalog()
        {
            Catalog.Clear();
            if (ObjectDB.instance)
            {
                foreach (var prefab in ObjectDB.instance.m_items)
                {
                    if (!prefab) continue;
                    var item = prefab.GetComponent<ItemDrop>();
                    if (item) Catalog.RegisterItem(item.m_itemData);
                }
                foreach (var effect in ObjectDB.instance.m_StatusEffects) Catalog.RegisterShieldStatus(effect);
            }
            foreach (var root in active.Keys)
            {
                if (!root) continue;
                var projectile = root.GetComponent<Projectile>(); if (projectile) Catalog.RegisterProjectile(projectile);
                var aoe = root.GetComponent<Aoe>(); if (aoe) Catalog.RegisterAoe(aoe);
            }
            bool conflict = false;
            foreach (var info in Chainloader.PluginInfos.Values)
                if (string.Equals(info.Metadata.Name, "SubtleShield", StringComparison.OrdinalIgnoreCase) || info.Metadata.GUID.IndexOf("subtleshield", StringComparison.OrdinalIgnoreCase) >= 0) conflict = true;
            if (conflict && !shieldConflict) Logger.LogWarning("SubtleShield is active. EffectsControl shield controls are suspended; disable SubtleShield and restart to use this mod's shield settings.");
            shieldConflict = conflict;
            rescanShields = true;
            optionsDirty = true;
            rebuild = false;
        }

        private void Update()
        {
            if (!ready) return;
            if (rebuild) Guard(RebuildCatalog);
            foreach (var state in active.Values)
                if (state.Stage.IsShield()) state.ReleaseLightWrites();
        }

        private void LateUpdate()
        {
            if (!ready) return;
            externalShieldMask = ReadExternalShieldMask();
            // A network object can arrive before ObjectDB/scene catalog initialization.
            // Resolve it only after the requested rebuild, using this client's settings.
            if (!rebuild)
            {
                foreach (var root in pendingNetwork) ObserveNetworkEffect(root);
                pendingNetwork.Clear();
            }
            RefreshOptions();
            // Instantiation invokes Awake on the root before some children. Defer component
            // capture and all visual writes until every native Awake/Start has completed, so
            // e.g. LightFlicker never captures an already scaled light as its native intensity.
            foreach (var pair in pending)
            {
                if (!pair.Key || active.ContainsKey(pair.Key)) continue;
                if (pair.Value.IsShield() && !CanControlShield(pair.Value))
                {
                    inactiveShields[pair.Key] = pair.Value;
                    continue;
                }
                Guard(() => {
                    var state = new VisualState(pair.Key, pair.Value);
                    if (state.Empty) state.Dispose(); else active.Add(pair.Key, state);
                });
            }
            pending.Clear();
            expired.Clear();
            foreach (var pair in active)
            {
                if (!pair.Key) { pair.Value.Dispose(); expired.Add(pair.Key); continue; }
                if (pair.Value.Stage.IsShield() && !CanControlShield(pair.Value.Stage))
                {
                    pair.Value.Dispose();
                    inactiveShields[pair.Key] = pair.Value.Stage;
                    expired.Add(pair.Key);
                    continue;
                }
                try { pair.Value.Apply(options[(int)pair.Value.Stage]); }
                catch (Exception error) { pair.Value.Dispose(); expired.Add(pair.Key); Report(error); }
            }
            foreach (var key in expired) active.Remove(key);
            expired.Clear();
            foreach (var pair in inactiveShields)
            {
                if (!pair.Key) expired.Add(pair.Key);
                else if (CanControlShield(pair.Value))
                {
                    // Capture next frame, after any other visual owner has restored its state.
                    pending[pair.Key] = pair.Value;
                    expired.Add(pair.Key);
                }
            }
            foreach (var key in expired) inactiveShields.Remove(key);
            if (!rebuild)
            {
                int mask = RequestedShieldMask() & ~externalShieldMask;
                int discover = rescanShields ? mask : mask & ~previousShieldMask;
                rescanShields = false;
                previousShieldMask = mask;
                if (discover != 0) Guard(() => DiscoverExistingShields(discover));
            }
        }

        private void DiscoverExistingShields(int mask)
        {
            foreach (var root in Resources.FindObjectsOfTypeAll<GameObject>())
                if (root && root.scene.IsValid() && root.scene.isLoaded && root.activeInHierarchy
                    && Catalog.TryEffect(root, out var stage) && (stage.ShieldBit() & mask) != 0)
                    Observe(root, stage);
        }

        internal void Observe(GameObject root, EffectStage stage)
        {
            if (!ready || !root || active.ContainsKey(root) || pending.ContainsKey(root) || inactiveShields.ContainsKey(root)) return;
            if (root.GetComponentInChildren<Character>(true) || root.GetComponentInChildren<Piece>(true)) return;
            pending.Add(root, stage);
        }

        private bool CanControlShield(EffectStage stage) =>
            ((RequestedShieldMask() & ~externalShieldMask) & stage.ShieldBit()) != 0;

        private int RequestedShieldMask() =>
            !ready || shieldConflict || !settings.Enabled.Value ? 0 :
            (settings.Shields.Value ? 1 : 0) | (settings.VengeanceEnabled.Value ? 2 : 0);

        private int ReadExternalShieldMask()
        {
            if (!Chainloader.PluginInfos.TryGetValue("Slikfoul.ImprovedMagicShield", out var info) || !info.Instance)
            {
                prioritySource = null; readExternalShieldMask = null;
                return 0;
            }
            if (prioritySource != info.Instance)
            {
                prioritySource = info.Instance;
                readExternalShieldMask = null;
                Guard(() => {
                    var connect = prioritySource.GetType().GetMethod("ConnectShieldAppearancePriority",
                        new[] { typeof(int), typeof(Action<int>), typeof(Func<int>) });
                    if (connect?.ReturnType == typeof(Func<int>))
                        readExternalShieldMask = (Func<int>)connect.Invoke(prioritySource,
                            new object[] { 1, (Action<int>)ReleaseShieldAppearance, (Func<int>)RequestedShieldMask });
                });
            }
            if (readExternalShieldMask != null)
            {
                // A faulty peer cannot leave both mods writing to the same visual.
                int mask = 3;
                Guard(() => mask = readExternalShieldMask() & 3);
                return mask;
            }
            // Older Improved Magic Shield permits its appearances when our protection
            // control is off. Preserve that behavior during a staggered update.
            if (shieldConflict || settings.Enabled.Value && settings.Shields.Value) return 0;
            var config = info.Instance.Config;
            if (!config.TryGetEntry("1 - General", "Enabled", out ConfigEntry<bool> enabled) || !enabled.Value) return 0;
            return (LegacyEnabled("3 - Magic Shield", "Magic Shield Effect") ? 1 : 0)
                | (LegacyEnabled("4 - Northern Vengeance", "Northern Vengeance Effect") ? 2 : 0);

            bool LegacyEnabled(string section, string key) =>
                config.TryGetEntry(section, key, out ConfigEntry<bool> entry) && entry.Value;
        }

        private void ReleaseShieldAppearance(int mask)
        {
            if (!ready) return;
            externalShieldMask |= mask & 3;
            expired.Clear();
            foreach (var pair in active)
            {
                var stage = pair.Value.Stage;
                if ((stage.ShieldBit() & mask) == 0) continue;
                pair.Value.Dispose();
                if (pair.Key) inactiveShields[pair.Key] = stage;
                expired.Add(pair.Key);
            }
            foreach (var root in expired) active.Remove(root);
        }

        private void RefreshOptions()
        {
            if (!optionsDirty) return;
            for (int i = 0; i < options.Length; i++) options[i] = settings.Get((EffectStage)i, shieldConflict);
            optionsDirty = false;
        }

        internal void ObserveProjectile(GameObject root)
        {
            if (!ready || !root) return;
            EffectStage stage;
            if (!Catalog.TryProjectile(root, out stage)) return;
            Guard(() => {
                var projectile = root.GetComponent<Projectile>(); if (projectile) Catalog.RegisterProjectile(projectile);
                var aoe = root.GetComponent<Aoe>(); if (aoe) Catalog.RegisterAoe(aoe);
                Observe(root, stage);
            });
        }

        internal void ObserveNetworkEffect(GameObject root)
        {
            if (!ready || !root) return;
            if (rebuild) { pendingNetwork.Add(root); return; }
            EffectStage stage;
            if (!Catalog.TryEffect(root, out stage)) return;
            Guard(() => {
                var projectile = root.GetComponent<Projectile>(); if (projectile) Catalog.RegisterProjectile(projectile);
                var aoe = root.GetComponent<Aoe>(); if (aoe) Catalog.RegisterAoe(aoe);
                Observe(root, stage);
            });
        }

        internal void Guard(Action action) { try { action(); } catch (Exception error) { Report(error); } }
        private void Report(Exception error)
        {
            if (failures++ < 5) Logger.LogError("Visual control failed; gameplay continues. " + error);
        }

        private void OnDestroy()
        {
            ready = false;
            if (harmony != null) harmony.UnpatchSelf();
            foreach (var state in active.Values) state.Dispose();
            active.Clear(); pending.Clear(); inactiveShields.Clear(); pendingNetwork.Clear(); Catalog.Clear(); Instance = null;
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class DatabaseAwakePatch { private static void Postfix() { if (Plugin.Instance) Plugin.Instance.RequestCatalog(); } }
    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class DatabaseCopyPatch { private static void Postfix() { if (Plugin.Instance) Plugin.Instance.RequestCatalog(); } }
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ScenePatch { private static void Postfix() { if (Plugin.Instance) Plugin.Instance.RequestCatalog(); } }

    [HarmonyPatch(typeof(Attack), "Start")]
    internal static class AttackPatch
    {
        private static void Prefix(Attack __instance, ItemDrop.ItemData weapon)
        {
            var plugin = Plugin.Instance;
            if (plugin && EffectCatalog.IsWeapon(weapon)) plugin.Guard(() => { plugin.Catalog.RegisterItem(weapon); plugin.Catalog.RegisterAttack(__instance); });
        }
    }

    [HarmonyPatch(typeof(EffectList), "Create")]
    internal static class EffectsPatch
    {
        private static void Postfix(EffectList __instance, GameObject[] __result)
        {
            var plugin = Plugin.Instance;
            if (!plugin || __result == null) return;
            EffectStage stage;
            if (plugin.Catalog.TryList(__instance, out stage)) foreach (var root in __result) plugin.Observe(root, stage);
        }
    }

    [HarmonyPatch(typeof(Projectile), "Awake")]
    internal static class ProjectileAwakePatch { private static void Postfix(Projectile __instance) { if (Plugin.Instance) Plugin.Instance.ObserveProjectile(__instance.gameObject); } }
    [HarmonyPatch(typeof(Aoe), "Awake")]
    internal static class AoeAwakePatch { private static void Postfix(Aoe __instance) { if (Plugin.Instance) Plugin.Instance.ObserveProjectile(__instance.gameObject); } }
    [HarmonyPatch(typeof(ZNetScene), "CreateObject")]
    internal static class NetworkEffectsPatch { private static void Postfix(GameObject __result) { if (Plugin.Instance && __result) Plugin.Instance.ObserveNetworkEffect(__result); } }

    [HarmonyPatch(typeof(Projectile), "Setup")]
    internal static class ProjectileSetupPatch
    {
        private static void Postfix(Projectile __instance, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            var p = Plugin.Instance;
            if (p && (EffectCatalog.IsWeapon(item) || EffectCatalog.IsWeapon(ammo))) p.Guard(() => {
                p.Catalog.RegisterProjectile(__instance); p.Observe(__instance.gameObject, EffectStage.Flight);
            });
        }
    }
    [HarmonyPatch(typeof(Aoe), "Setup")]
    internal static class AoeSetupPatch
    {
        private static void Postfix(Aoe __instance, ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            var p = Plugin.Instance;
            if (p && (EffectCatalog.IsWeapon(item) || EffectCatalog.IsWeapon(ammo))) p.Guard(() => {
                p.Catalog.RegisterAoe(__instance); p.Observe(__instance.gameObject, EffectStage.Impact);
            });
        }
    }
    [HarmonyPatch(typeof(SE_Shield), "Setup")]
    internal static class ShieldPatch
    {
        private static void Prefix(SE_Shield __instance) { var p = Plugin.Instance; if (p) p.Guard(() => p.Catalog.RegisterShield(__instance)); }
    }

    [HarmonyPatch(typeof(StatusEffect), "Setup")]
    internal static class ShieldStatusPatch
    {
        private static void Prefix(StatusEffect __instance)
        {
            var p = Plugin.Instance;
            if (p && __instance is SE_React) p.Guard(() => p.Catalog.RegisterShieldStatus(__instance));
        }
    }
}
