using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using System.Collections.Generic;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.22: Stores ability selection context for use across different AI phases
    /// Ported from EnhancedCompanionAI for movement decision integration
    /// </summary>
    public static class AbilityContextStore
    {
        private static Dictionary<string, StoredAbilityInfo> _abilityCache = new Dictionary<string, StoredAbilityInfo>();

        public class StoredAbilityInfo
        {
            public string AbilityName;
            public int EffectiveRange;  // Range of the ability (weapon or spell)
            public bool IsRangedWeapon;
            public bool IsWeaponAbility;
            public bool NeedsMoveFirst;  // True if ability requires moving away from melee first
            public int MinRange;  // Minimum range requirement
        }

        /// <summary>
        /// Store the top-priority ability for a unit
        /// </summary>
        public static void StoreAbilityInfo(BaseUnitEntity unit, AbilityData ability, bool needsMoveFirst = false)
        {
            if (unit == null || ability == null)
                return;

            var info = new StoredAbilityInfo
            {
                AbilityName = ability.Name,
                NeedsMoveFirst = needsMoveFirst
            };

            // Get ability range
            int range = 0;
            int minRange = 0;

            // Check if it's a weapon-based ability
            if (ability.Weapon != null && ability.Weapon.Blueprint != null)
            {
                info.IsWeaponAbility = true;
                range = ability.Weapon.Blueprint.AttackRange;
                info.IsRangedWeapon = range >= 3;
            }
            else
            {
                info.IsWeaponAbility = false;
                info.IsRangedWeapon = false;

                // For non-weapon abilities, get the blueprint range
                if (ability.Blueprint != null)
                {
                    range = ability.Blueprint.GetRange();

                    // If range is very high (100000), it's unlimited - treat as ranged
                    if (range > 1000)
                    {
                        range = 100;  // Cap at 100m for practical purposes
                    }
                }
            }

            // Get minimum range (important for ranged weapons)
            minRange = ability.MinRangeCells;

            info.EffectiveRange = range;
            info.MinRange = minRange;

            _abilityCache[unit.UniqueId] = info;

            Main.LogDebug($"[AbilityContextStore] Stored ability for {unit.CharacterName}: {ability.Name} (Range: {range}, MinRange: {minRange}, IsRanged: {info.IsRangedWeapon}, NeedsMoveFirst: {needsMoveFirst})");
        }

        /// <summary>
        /// Check if unit has a stored ability that needs movement first
        /// </summary>
        public static bool NeedsMoveFirst(BaseUnitEntity unit)
        {
            if (unit == null)
                return false;

            if (_abilityCache.TryGetValue(unit.UniqueId, out var info))
                return info.NeedsMoveFirst;

            return false;
        }

        /// <summary>
        /// Get stored ability info for a unit
        /// </summary>
        public static StoredAbilityInfo GetAbilityInfo(BaseUnitEntity unit)
        {
            if (unit == null)
                return null;

            if (_abilityCache.TryGetValue(unit.UniqueId, out var info))
                return info;

            return null;
        }

        /// <summary>
        /// Clear stored info for a unit (call after action completes)
        /// </summary>
        public static void ClearAbilityInfo(BaseUnitEntity unit)
        {
            if (unit != null)
                _abilityCache.Remove(unit.UniqueId);
        }

        /// <summary>
        /// Clear all stored info (call at end of combat)
        /// </summary>
        public static void ClearAll()
        {
            _abilityCache.Clear();
            Main.LogDebug("[AbilityContextStore] Cleared all stored ability info");
        }
    }
}
