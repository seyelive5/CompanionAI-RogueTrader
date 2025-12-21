using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using UnityEngine;

namespace CompanionAI_v2.Core
{
    /// <summary>
    /// v2.1.0: 전투 헬퍼 - 모든 전략에서 공통으로 사용
    /// </summary>
    public static class CombatHelpers
    {
        /// <summary>
        /// 능력이 AoE인지 확인 (여러 방법으로 체크)
        /// 주의: 무기 버스트 공격은 AoE가 아님!
        /// </summary>
        public static bool IsAoEAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // ★ 무기 공격은 AoE가 아님 (버스트 포함)
            // 무기에서 온 능력이면서 단일 타겟이면 AoE 아님
            if (ability.Weapon != null)
            {
                // 무기 공격의 버스트/연사는 단일 타겟
                if (bpName.Contains("burstfire") || bpName.Contains("burst_fire") ||
                    bpName.Contains("singleshot") || bpName.Contains("fullburst") ||
                    bpName.Contains("meleeattack") || bpName.Contains("swords"))
                    return false;

                // 한글 무기 공격
                if (name.Contains("난사") || name.Contains("사격") || name.Contains("타격"))
                    return false;
            }

            // 1. 게임 API 체크
            if (ability.IsAOE) return true;
            if (ability.GetPatternSettings() != null) return true;

            // 2. 블루프린트 체크
            var bp = ability.Blueprint;
            if (bp != null)
            {
                // CanTargetPoint는 종종 AoE를 의미
                if (bp.CanTargetPoint) return true;
            }

            // 3. 이름 기반 체크 (백업) - 더 구체적으로
            // 한글 AoE 키워드
            if (name.Contains("응시") || name.Contains("폭발") ||
                name.Contains("광역") || name.Contains("파동") ||
                name.Contains("와류"))
                return true;

            // 영어 AoE 키워드 - "burst"는 무기가 아닌 경우만
            if (name.Contains("stare") || name.Contains("explod") ||
                name.Contains("area") || name.Contains("cone") ||
                name.Contains("line") || name.Contains("wave") ||
                name.Contains("blast") || name.Contains("scream"))
                return true;

            // 블루프린트 이름 키워드 - burst는 무기가 아닌 경우만
            if (bpName.Contains("aoe") || bpName.Contains("area") ||
                bpName.Contains("cone") || bpName.Contains("line") ||
                bpName.Contains("lidless") || bpName.Contains("scatter") ||
                bpName.Contains("warpfire") || bpName.Contains("psychicscream"))
                return true;

            // 사이킥 능력 중 특정 패턴 (psychic + 특정 키워드)
            if (bpName.Contains("psychic") &&
                (bpName.Contains("stare") || bpName.Contains("scream") || bpName.Contains("blast")))
                return true;

            return false;
        }

        /// <summary>
        /// 적 위치 근처에 아군이 있는지 확인
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
        /// 특정 위치 근처의 아군 수 계산
        /// </summary>
        public static int CountAlliesNearPosition(
            BaseUnitEntity caster,
            Vector3 position,
            List<BaseUnitEntity> allies,
            float radius = 7f)
        {
            int count = 0;

            // 아군 체크
            if (allies != null)
            {
                foreach (var ally in allies)
                {
                    if (ally == null || ally.LifeState.IsDead) continue;

                    float distance = Vector3.Distance(ally.Position, position);
                    if (distance <= radius)
                    {
                        count++;
                        Main.LogDebug($"[CombatHelper] Ally {ally.CharacterName} is {distance:F1}m from target");
                    }
                }
            }

            // 캐스터 자신도 체크 (자기 자신이 범위에 있을 수 있음)
            if (caster != null)
            {
                float selfDistance = Vector3.Distance(caster.Position, position);
                if (selfDistance <= radius && selfDistance > 0.5f)  // 0.5m 이상 떨어져 있어야
                {
                    count++;
                    Main.LogDebug($"[CombatHelper] Self is {selfDistance:F1}m from target");
                }
            }

            return count;
        }

        /// <summary>
        /// AoE 능력 사용이 안전한지 확인
        /// </summary>
        public static bool IsAoESafe(
            AbilityData ability,
            BaseUnitEntity caster,
            BaseUnitEntity enemy,
            List<BaseUnitEntity> allies)
        {
            // AoE 아니면 안전
            if (!IsAoEAbility(ability)) return true;

            // 적 근처 아군 수 체크 (10m 반경 - AoE 안전 마진 확보)
            int alliesNear = CountAlliesNearEnemy(caster, enemy, allies, 10f);

            if (alliesNear > 0)
            {
                Main.LogDebug($"[CombatHelper] AoE {ability.Name} BLOCKED: {alliesNear} allies near {enemy?.CharacterName}");
                return false;
            }

            return true;
        }
    }
}
