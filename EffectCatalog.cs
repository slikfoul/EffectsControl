using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace EffectsControl
{
    // The catalog stores provenance, never rewrites prefabs or gameplay fields.
    public sealed class EffectCatalog
    {
        private ConditionalWeakTable<EffectList, StageBox> lists = new ConditionalWeakTable<EffectList, StageBox>();
        private sealed class StageBox { public EffectStage Stage; }
        private int registeredLists;
        private readonly Dictionary<string, EffectStage> projectiles = new Dictionary<string, EffectStage>(StringComparer.Ordinal);
        private readonly Dictionary<string, EffectStage> effects = new Dictionary<string, EffectStage>(StringComparer.Ordinal);
        private readonly HashSet<GameObject> visited = new HashSet<GameObject>();
        public int ListCount { get { return registeredLists; } }
        public int ProjectileCount { get { return projectiles.Count; } }
        public void Clear() { lists = new ConditionalWeakTable<EffectList, StageBox>(); registeredLists = 0; projectiles.Clear(); effects.Clear(); visited.Clear(); }
        public bool TryList(EffectList list, out EffectStage stage)
        {
            StageBox value;
            if (list != null && lists.TryGetValue(list, out value)) { stage = value.Stage; return true; }
            stage = EffectStage.Launch; return false;
        }
        public bool TryProjectile(GameObject instance, out EffectStage stage)
        {
            return TryInstance(projectiles, instance, out stage);
        }
        public bool TryEffect(GameObject instance, out EffectStage stage)
        {
            return TryInstance(effects, instance, out stage);
        }
        private static bool TryInstance(Dictionary<string, EffectStage> prefabs, GameObject instance, out EffectStage stage)
        {
            stage = EffectStage.Launch;
            if (!instance) return false;
            string name = instance.name;
            if (name.EndsWith("(Clone)", StringComparison.Ordinal)) name = name.Substring(0, name.Length - 7);
            return prefabs.TryGetValue(name, out stage);
        }

        public static bool IsWeapon(ItemDrop.ItemData item)
        {
            if (item == null || item.m_shared == null) return false;
            switch (item.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable: return true;
                default: return false;
            }
        }

        public void RegisterItem(ItemDrop.ItemData item)
        {
            if (!IsWeapon(item)) return;
            var shared = item.m_shared;
            RegisterAttack(shared.m_attack); RegisterAttack(shared.m_secondaryAttack);
            Add(shared.m_startEffect, EffectStage.Launch); Add(shared.m_holdStartEffect, EffectStage.Launch);
            Add(shared.m_trailStartEffect, EffectStage.Launch);
            Add(shared.m_triggerEffect, shared.m_attack != null && shared.m_attack.m_attackType == Attack.AttackType.Area ? EffectStage.Impact : EffectStage.Launch);
            Add(shared.m_hitEffect, EffectStage.Impact); Add(shared.m_hitTerrainEffect, EffectStage.Impact);
            Visit(shared.m_spawnOnHit, EffectStage.Impact); Visit(shared.m_spawnOnHitTerrain, EffectStage.Impact);
        }

        public void RegisterAttack(Attack attack)
        {
            if (attack == null) return;
            Add(attack.m_startEffect, EffectStage.Launch); Add(attack.m_trailStartEffect, EffectStage.Launch);
            Add(attack.m_burstEffect, EffectStage.Launch);
            Add(attack.m_triggerEffect, attack.m_attackType == Attack.AttackType.Area ? EffectStage.Impact : EffectStage.Launch);
            Add(attack.m_hitEffect, EffectStage.Impact); Add(attack.m_hitTerrainEffect, EffectStage.Impact);
            Visit(attack.m_attackProjectile, EffectStage.Flight);
            Visit(attack.m_spawnOnTrigger, EffectStage.Impact); Visit(attack.m_spawnOnHit, EffectStage.Impact);
        }

        public void RegisterProjectile(Projectile projectile)
        {
            Add(projectile.m_hitEffects, EffectStage.Impact); Add(projectile.m_hitWaterEffects, EffectStage.Impact);
            Add(projectile.m_spawnOnHitEffects, EffectStage.Impact);
            Visit(projectile.m_spawnOnHit, EffectStage.Impact);
            foreach (var prefab in projectile.m_randomSpawnOnHit) Visit(prefab, EffectStage.Impact);
        }

        public void RegisterAoe(Aoe aoe)
        {
            Add(aoe.m_hitEffects, EffectStage.Impact); Add(aoe.m_initiateEffect, EffectStage.Impact);
            Add(aoe.m_chainEffects, EffectStage.Impact);
            Visit(aoe.m_chainObj, EffectStage.Impact); Visit(aoe.m_spawnOnHitTerrain, EffectStage.Impact);
        }

        public void RegisterShield(SE_Shield shield)
        {
            Add(shield.m_startEffects, EffectStage.Shield);
            Add(shield.m_hitEffects, EffectStage.Shield); Add(shield.m_breakEffects, EffectStage.Shield);
            Add(shield.m_stopEffects, EffectStage.Shield);
        }

        private void Add(EffectList list, EffectStage stage)
        {
            if (list == null || list.m_effectPrefabs == null || list.m_effectPrefabs.Length == 0) return;
            // Shield takes precedence when a list is intentionally shared.
            StageBox value;
            if (!lists.TryGetValue(list, out value)) { lists.Add(list, new StageBox { Stage = stage }); registeredLists++; }
            else if (stage == EffectStage.Shield) value.Stage = stage;
            foreach (var effect in list.m_effectPrefabs) if (effect != null) Visit(effect.m_prefab, stage);
        }

        private void Visit(GameObject prefab, EffectStage stage)
        {
            if (!prefab) return;
            // Do not recurse into summoned creatures, placed pieces or ambient status effects.
            if (prefab.GetComponentInChildren<Character>(true) || prefab.GetComponentInChildren<Piece>(true)) return;
            var projectile = prefab.GetComponent<Projectile>();
            var aoe = prefab.GetComponent<Aoe>();
            EffectStage prefabStage = stage == EffectStage.Shield ? stage : (aoe ? EffectStage.Impact : stage);
            // Remote ZDO copies bypass EffectList.Create. Keep every linked effect prefab,
            // including plain particle/light objects and shield bubbles, for that path.
            if (!effects.ContainsKey(prefab.name) || prefabStage == EffectStage.Shield) effects[prefab.name] = prefabStage;
            if (!visited.Add(prefab)) return;
            if (projectile || aoe)
            {
                projectiles[prefab.name] = aoe ? EffectStage.Impact : stage;
                if (projectile) RegisterProjectile(projectile);
                if (aoe) RegisterAoe(aoe);
            }
        }
    }
}
