using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
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
        public const string PluginVersion = "1.0.1";
        internal static Plugin Instance;
        internal readonly EffectCatalog Catalog = new EffectCatalog();
        private readonly Dictionary<GameObject, VisualState> active = new Dictionary<GameObject, VisualState>();
        private readonly Dictionary<GameObject, EffectStage> pending = new Dictionary<GameObject, EffectStage>();
        private readonly HashSet<GameObject> pendingNetwork = new HashSet<GameObject>();
        private readonly List<GameObject> expired = new List<GameObject>();
        private readonly VisualOptions[] options = new VisualOptions[4];
        private Settings settings;
        private Harmony harmony;
        private bool rebuild = true, ready, shieldConflict;
        private bool optionsDirty = true;
        private int failures;

        private void Awake()
        {
            Instance = this;
            settings = new Settings(Config);
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
                foreach (var effect in ObjectDB.instance.m_StatusEffects)
                {
                    var shield = effect as SE_Shield;
                    if (shield) Catalog.RegisterShield(shield);
                }
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
            optionsDirty = true;
            rebuild = false;
        }

        private void Update()
        {
            if (ready && rebuild) Guard(RebuildCatalog);
        }

        private void LateUpdate()
        {
            if (!ready) return;
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
                if (!pair.Key || active.ContainsKey(pair.Key) || (pair.Value == EffectStage.Shield && shieldConflict)) continue;
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
                try { pair.Value.Apply(options[(int)pair.Value.Stage]); }
                catch (Exception error) { pair.Value.Dispose(); expired.Add(pair.Key); Report(error); }
            }
            foreach (var key in expired) active.Remove(key);
        }

        internal void Observe(GameObject root, EffectStage stage)
        {
            if (!ready || !root || active.ContainsKey(root) || pending.ContainsKey(root) || (stage == EffectStage.Shield && shieldConflict)) return;
            if (root.GetComponentInChildren<Character>(true) || root.GetComponentInChildren<Piece>(true)) return;
            pending.Add(root, stage);
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
            active.Clear(); pending.Clear(); pendingNetwork.Clear(); Catalog.Clear(); Instance = null;
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
}
