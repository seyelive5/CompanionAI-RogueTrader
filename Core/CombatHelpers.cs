using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using UnityEngine;
using CompanionAI_v2_2.Settings;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.0: 전투 헬퍼 - 공통 유틸리티
    /// </summary>
    public static class CombatHelpers
    {
        /// <summary>
        /// 능력이 AoE인지 확인
        /// </summary>
        public static bool IsAoEAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // 무기 공격은 AoE가 아님
            if (ability.Weapon != null)
            {
                if (bpName.Contains("burstfire") || bpName.Contains("burst_fire") ||
                    bpName.Contains("singleshot") || bpName.Contains("fullburst") ||
                    bpName.Contains("meleeattack") || bpName.Contains("swords"))
                    return false;

                if (name.Contains("난사") || name.Contains("사격") || name.Contains("타격"))
                    return false;
            }

            // 게임 API 체크
            if (ability.IsAOE) return true;
            if (ability.GetPatternSettings() != null) return true;

            var bp = ability.Blueprint;
            if (bp != null && bp.CanTargetPoint) return true;

            // 이름 기반 체크
            if (name.Contains("응시") || name.Contains("폭발") ||
                name.Contains("광역") || name.Contains("파동"))
                return true;

            if (name.Contains("stare") || name.Contains("explod") ||
                name.Contains("area") || name.Contains("cone") ||
                name.Contains("line") || name.Contains("wave") ||
                name.Contains("blast") || name.Contains("scream"))
                return true;

            if (bpName.Contains("aoe") || bpName.Contains("area") ||
                bpName.Contains("cone") || bpName.Contains("line") ||
                bpName.Contains("lidless") || bpName.Contains("scatter"))
                return true;

            return false;
        }

        /// <summary>
        /// 적 위치 근처의 아군 수
        /// </summary>
        public static int CountAlliesNearEnemy(
            BaseUnitEntity caster,
            BaseUnitEntity enemy,
            List<BaseUnitEntity> allies,
            float radius = 7f)
        {
            if (enemy == null) return 0;
            return CountAlliesNearPosition(caster, enemy.Position, allies, radius);
        }

        /// <summary>
        /// 특정 위치 근처의 아군 수
        /// </summary>
        public static int CountAlliesNearPosition(
            BaseUnitEntity caster,
            Vector3 position,
            List<BaseUnitEntity> allies,
            float radius = 7f)
        {
            int count = 0;

            if (allies != null)
            {
                foreach (var ally in allies)
                {
                    if (ally == null || ally.LifeState.IsDead) continue;

                    float distance = Vector3.Distance(ally.Position, position);
                    if (distance <= radius)
                    {
                        count++;
                    }
                }
            }

            if (caster != null)
            {
                float selfDistance = Vector3.Distance(caster.Position, position);
                if (selfDistance <= radius && selfDistance > 0.5f)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// AoE 능력 사용이 안전한지 확인
        /// ★ v2.2.34: 반경 2.5m, 아군 2명까지 허용 (완화)
        /// </summary>
        public static bool IsAoESafe(
            AbilityData ability,
            BaseUnitEntity caster,
            BaseUnitEntity enemy,
            List<BaseUnitEntity> allies)
        {
            if (!IsAoEAbility(ability)) return true;

            // ★ v2.2.34: 반경 축소 (3f -> 2.5f), 아군 허용 증가 (1 -> 2)
            int alliesNear = CountAlliesNearEnemy(caster, enemy, allies, 2.5f);

            // 2명까지 허용 (전투 상황에서 다소 피해는 감수)
            return alliesNear <= 2;
        }

        /// <summary>
        /// ★ v2.2.1: 수류탄/폭발물인지 확인
        /// </summary>
        public static bool IsGrenadeOrExplosive(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // 한국어/영어 수류탄 키워드
            if (name.Contains("수류탄") || name.Contains("폭탄") ||
                name.Contains("grenade") || name.Contains("explosive") ||
                name.Contains("krak") || name.Contains("frag"))
                return true;

            if (bpName.Contains("grenade") || bpName.Contains("explosive") ||
                bpName.Contains("throwable") || bpName.Contains("thrown") ||
                bpName.Contains("krak") || bpName.Contains("frag"))
                return true;

            return false;
        }

        /// <summary>
        /// ★ v2.2.1: 특정 위치 근처의 적 수
        /// </summary>
        public static int CountEnemiesNearPosition(
            Vector3 position,
            List<BaseUnitEntity> enemies,
            float radius = 5f)
        {
            int count = 0;

            if (enemies != null)
            {
                foreach (var enemy in enemies)
                {
                    if (enemy == null || enemy.LifeState.IsDead) continue;

                    float distance = Vector3.Distance(enemy.Position, position);
                    if (distance <= radius)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// ★ v2.2.1: 수류탄 사용이 효율적인지 확인 (적 2명 이상)
        /// </summary>
        public static bool IsGrenadeEfficient(
            AbilityData ability,
            BaseUnitEntity target,
            List<BaseUnitEntity> allEnemies)
        {
            if (!IsGrenadeOrExplosive(ability)) return true; // 수류탄이 아니면 통과

            if (target == null) return false;

            // 타겟 주변 적 수 확인 (5m 반경)
            int enemiesNear = CountEnemiesNearPosition(target.Position, allEnemies, 5f);

            // 2명 이상일 때만 효율적
            return enemiesNear >= 2;
        }

        /// <summary>
        /// ★ v2.2.34: AoE 공격이 효율적인지 확인 (완화)
        /// - 적 2명 이상 + 아군 안전
        /// </summary>
        public static bool ShouldUseAoE(
            AbilityData ability,
            BaseUnitEntity caster,
            BaseUnitEntity target,
            List<BaseUnitEntity> allEnemies,
            List<BaseUnitEntity> allies)
        {
            if (!IsAoEAbility(ability)) return false;

            if (target == null) return false;

            // ★ v2.2.34: 아군 안전성 완화 (반경 2.5m, 2명까지 허용)
            int alliesNear = CountAlliesNearEnemy(caster, target, allies, 2.5f);
            if (alliesNear > 2) return false; // 아군 3명 이상이면 안전하지 않음

            // 적 수 확인 (5m 반경) - 2명 이상이면 효율적
            int enemiesNear = CountEnemiesNearPosition(target.Position, allEnemies, 5f);
            return enemiesNear >= 2;
        }

        /// <summary>
        /// ★ v2.2.1: 무기 공격 유형 분류
        /// </summary>
        public static WeaponAttackType GetWeaponAttackType(AbilityData ability)
        {
            if (ability == null) return WeaponAttackType.Unknown;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // 수류탄/폭발물
            if (IsGrenadeOrExplosive(ability))
                return WeaponAttackType.Grenade;

            // 산탄/확산 (Scatter, Full Burst 등)
            if (bpName.Contains("scatter") || bpName.Contains("fullburst") ||
                bpName.Contains("full_burst") || name.Contains("난사") ||
                name.Contains("확산") || name.Contains("산탄"))
                return WeaponAttackType.Scatter;

            // 점사 (Burst Fire)
            if (bpName.Contains("burstfire") || bpName.Contains("burst_fire") ||
                name.Contains("점사") || name.Contains("연사"))
                return WeaponAttackType.Burst;

            // 단발 (Single Shot)
            if (bpName.Contains("singleshot") || bpName.Contains("single_shot") ||
                name.Contains("단발") || name.Contains("정조준"))
                return WeaponAttackType.Single;

            // 근접
            if (bpName.Contains("melee") || bpName.Contains("sword") ||
                bpName.Contains("axe") || ability.IsMelee)
                return WeaponAttackType.Melee;

            // 기본값
            if (IsAoEAbility(ability))
                return WeaponAttackType.Scatter;

            return WeaponAttackType.Single;
        }

        /// <summary>
        /// ★ v2.2.1: 상황에 맞는 최적 공격 선택
        /// ★ v2.2.10: 무기 공격 우선 (스킬 공격보다 낮은 우선순위 값)
        /// </summary>
        public static int GetAttackPriority(
            AbilityData ability,
            BaseUnitEntity caster,
            BaseUnitEntity target,
            List<BaseUnitEntity> allEnemies,
            List<BaseUnitEntity> allies)
        {
            var attackType = GetWeaponAttackType(ability);
            int enemiesNear = target != null ? CountEnemiesNearPosition(target.Position, allEnemies, 5f) : 1;
            int alliesNear = target != null ? CountAlliesNearEnemy(caster, target, allies, 3f) : 0;

            // ★ v2.2.10: 무기 공격 여부 확인
            bool isWeaponAttack = ability.Weapon != null;

            // 기본 우선순위 (낮을수록 우선)
            // ★ 무기 공격은 기본적으로 스킬 공격보다 우선
            int priority = isWeaponAttack ? 50 : 150;

            switch (attackType)
            {
                case WeaponAttackType.Single:
                    // 단일 타겟: 적 1명일 때 최적
                    priority = isWeaponAttack
                        ? (enemiesNear == 1 ? 5 : 15)   // 무기 공격
                        : (enemiesNear == 1 ? 50 : 80); // 스킬 공격
                    break;

                case WeaponAttackType.Burst:
                    // 점사: 적 1-2명일 때 적합
                    priority = isWeaponAttack
                        ? (enemiesNear <= 2 ? 8 : 20)
                        : (enemiesNear <= 2 ? 60 : 90);
                    break;

                case WeaponAttackType.Scatter:
                    // ★ v2.2.34: 산탄/확산 완화 - 아군 2명까지 허용
                    if (enemiesNear >= 2 && alliesNear <= 2)
                        priority = isWeaponAttack ? 3 : 40;
                    else if (alliesNear > 2)
                        priority = 200; // 아군 3명 이상 - 사용 안함
                    else
                        priority = isWeaponAttack ? 25 : 70;
                    break;

                case WeaponAttackType.Grenade:
                    // ★ v2.2.34: 수류탄 완화 - 아군 1명까지 허용
                    if (enemiesNear >= 2 && alliesNear <= 1)
                        priority = 2; // 최우선 (수류탄은 효율적일 때만)
                    else if (alliesNear > 1)
                        priority = 300; // 아군 2명 이상 - 사용 안함
                    else
                        priority = 150; // 적 1명만 - 비효율
                    break;

                case WeaponAttackType.Melee:
                    // 근접: 무기 공격 우선
                    priority = isWeaponAttack ? 10 : 55;
                    break;

                default:
                    priority = isWeaponAttack ? 50 : 150;
                    break;
            }

            return priority;
        }

        #region ★ v2.2.40: RangePreference 중앙화 헬퍼
        // ⚠️ IMPORTANT: RangePreference 로직은 반드시 이 헬퍼 함수들을 사용
        // Role 기반 추론 금지 (예: Role==DPS → preferRanged 같은 레거시 패턴)

        /// <summary>
        /// ★ v2.2.40: 원거리 선호 여부 (Single Source of Truth)
        /// </summary>
        public static bool ShouldPreferRanged(CharacterSettings settings)
        {
            if (settings == null) return false;
            return settings.RangePreference == RangePreference.PreferRanged ||
                   settings.RangePreference == RangePreference.MaintainRange;
        }

        /// <summary>
        /// ★ v2.2.40: 근접 선호 여부 (Single Source of Truth)
        /// </summary>
        public static bool ShouldPreferMelee(CharacterSettings settings)
        {
            if (settings == null) return false;
            return settings.RangePreference == RangePreference.PreferMelee;
        }

        /// <summary>
        /// ★ v2.2.40: 능력이 선호하는 무기 타입인지 확인
        /// </summary>
        public static bool IsPreferredWeaponType(AbilityData ability, RangePreference preference)
        {
            if (ability == null) return false;

            var weaponType = GetWeaponAttackType(ability);
            bool isRanged = weaponType == WeaponAttackType.Single ||
                           weaponType == WeaponAttackType.Burst ||
                           weaponType == WeaponAttackType.Scatter;
            bool isMelee = weaponType == WeaponAttackType.Melee;

            switch (preference)
            {
                case RangePreference.PreferRanged:
                case RangePreference.MaintainRange:
                    return isRanged;
                case RangePreference.PreferMelee:
                    return isMelee;
                default: // Adaptive
                    return true;
            }
        }

        /// <summary>
        /// ★ v2.2.40: RangePreference에 따라 능력 리스트 필터링 (중앙화)
        /// 원거리/근접 옵션이 없으면 폴백 허용
        /// </summary>
        public static List<AbilityData> FilterAbilitiesByRangePreference(
            List<AbilityData> abilities,
            RangePreference preference)
        {
            if (abilities == null || abilities.Count == 0)
                return abilities;

            if (preference == RangePreference.PreferRanged || preference == RangePreference.MaintainRange)
            {
                var rangedOnly = abilities.Where(a => {
                    var weaponType = GetWeaponAttackType(a);
                    return weaponType == WeaponAttackType.Single ||
                           weaponType == WeaponAttackType.Burst ||
                           weaponType == WeaponAttackType.Scatter;
                }).ToList();

                if (rangedOnly.Count > 0)
                {
                    Main.LogDebug($"[CombatHelpers] RangeFilter: {preference} - {rangedOnly.Count} ranged (filtered {abilities.Count - rangedOnly.Count} melee)");
                    return rangedOnly;
                }
                // 원거리 없으면 폴백
                Main.LogDebug($"[CombatHelpers] No ranged abilities - fallback to all");
            }
            else if (preference == RangePreference.PreferMelee)
            {
                var meleeOnly = abilities.Where(a => {
                    var weaponType = GetWeaponAttackType(a);
                    return weaponType == WeaponAttackType.Melee;
                }).ToList();

                if (meleeOnly.Count > 0)
                {
                    Main.LogDebug($"[CombatHelpers] RangeFilter: PreferMelee - {meleeOnly.Count} melee (filtered {abilities.Count - meleeOnly.Count} ranged)");
                    return meleeOnly;
                }
                // 근접 없으면 폴백
                Main.LogDebug($"[CombatHelpers] No melee abilities - fallback to all");
            }

            return abilities;  // Adaptive: 필터 없음
        }

        #endregion
    }

    /// <summary>
    /// 무기 공격 유형
    /// </summary>
    public enum WeaponAttackType
    {
        Unknown,
        Single,     // 단발
        Burst,      // 점사
        Scatter,    // 산탄/확산 AoE
        Grenade,    // 수류탄/폭발물
        Melee       // 근접
    }
}
