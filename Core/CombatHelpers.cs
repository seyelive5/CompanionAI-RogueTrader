using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using UnityEngine;

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
        /// ★ v2.2.1: 반경 축소 (10f -> 3f), 1명까지 허용
        /// </summary>
        public static bool IsAoESafe(
            AbilityData ability,
            BaseUnitEntity caster,
            BaseUnitEntity enemy,
            List<BaseUnitEntity> allies)
        {
            if (!IsAoEAbility(ability)) return true;

            // ★ 실제 AoE 피해 반경은 보통 2-3m
            int alliesNear = CountAlliesNearEnemy(caster, enemy, allies, 3f);

            // 1명까지는 허용 (완벽한 상황은 드물므로)
            return alliesNear <= 1;
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
        /// ★ v2.2.1: AoE 공격이 효율적인지 확인
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

            // 아군 안전성 확인 (3m 반경)
            int alliesNear = CountAlliesNearEnemy(caster, target, allies, 3f);
            if (alliesNear > 1) return false; // 아군 2명 이상이면 안전하지 않음

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
                    // 산탄/확산: 적 2명 이상 + 아군 안전할 때
                    if (enemiesNear >= 2 && alliesNear <= 1)
                        priority = isWeaponAttack ? 3 : 40;
                    else if (alliesNear > 1)
                        priority = 200; // 아군 위험 - 사용 안함
                    else
                        priority = isWeaponAttack ? 25 : 70;
                    break;

                case WeaponAttackType.Grenade:
                    // 수류탄: 적 2명 이상 필수
                    if (enemiesNear >= 2 && alliesNear == 0)
                        priority = 2; // 최우선 (수류탄은 효율적일 때만)
                    else
                        priority = 300; // 비효율 - 사용 안함
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
