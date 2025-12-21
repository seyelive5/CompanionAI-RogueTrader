using System.Collections.Generic;
using System.Linq;
using CompanionAI_v2.Core;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;

namespace CompanionAI_v2.Strategies
{
    /// <summary>
    /// v2.1.0: Tank 전략 - 우선순위 기반 결정
    ///
    /// 우선순위:
    /// 1. 도발 (적이 많고 아군이 위험할 때)
    /// 2. 방어 버프 (자신에게)
    /// 3. 근접 공격 (가장 가까운 적)
    /// 4. 이동 (적에게 접근)
    /// 5. 원거리 공격 (근접 불가 시)
    /// </summary>
    public class TankStrategy : IUnitStrategy
    {
        public string StrategyName => "Tank";

        // HP 소모 스킬 안전 임계값 (이 HP% 이하면 HP 소모 스킬 사용 안함)
        private const float HP_COST_ABILITY_THRESHOLD = 40f;

        // 이번 턴에 사용한 버프 추적 (반복 방지)
        private static HashSet<string> _usedBuffsThisTurn = new HashSet<string>();
        private static string _lastUnitId = null;

        public ActionDecision DecideAction(ActionContext ctx)
        {
            // 유닛이 바뀌면 버프 추적 초기화
            string unitId = ctx.Unit.UniqueId;
            if (_lastUnitId != unitId)
            {
                _usedBuffsThisTurn.Clear();
                _lastUnitId = unitId;
            }

            // ★ Veil 및 Momentum 상태 로깅
            Main.Log($"[Tank] {ctx.Unit.CharacterName}: HP={ctx.HPPercent:F0}%, {GameAPI.GetVeilStatusString()}, {GameAPI.GetMomentumStatusString()}, Enemies={ctx.Enemies.Count}, InMelee={ctx.IsInMeleeRange}");

            // 1. 도발이 필요하고 가능한가?
            var tauntResult = TryUseTaunt(ctx);
            if (tauntResult != null) return tauntResult;

            // 2. 방어 자세가 필요한가? (적에게 둘러싸여 있거나 HP 낮을 때)
            var stanceResult = TryUseDefensiveStance(ctx);
            if (stanceResult != null) return stanceResult;

            // 3. 방어 버프가 필요한가? (HP 낮을 때)
            var buffResult = TryUseDefensiveBuff(ctx);
            if (buffResult != null) return buffResult;

            // 3. 근접 공격이 가능한가?
            var meleeResult = TryMeleeAttack(ctx);
            if (meleeResult != null) return meleeResult;

            // 4. 근접 범위에 적이 없으면 이동
            if (!ctx.IsInMeleeRange && ctx.CanMove)
            {
                return ActionDecision.Move("No enemy in melee range, advancing");
            }

            // 5. 원거리 공격 (이동도 못하고 근접도 못할 때)
            var rangedResult = TryRangedAttack(ctx);
            if (rangedResult != null) return rangedResult;

            // 6. 공격할 수 없으면 자기 버프 (AP 남았을 때)
            var selfBuffResult = TryUseSelfBuff(ctx);
            if (selfBuffResult != null) return selfBuffResult;

            // 아무것도 할 수 없음
            return ActionDecision.EndTurn("No valid action available");
        }

        /// <summary>
        /// 도발 시도
        /// </summary>
        private ActionDecision TryUseTaunt(ActionContext ctx)
        {
            // 도발 조건: 근처에 적이 2명 이상이고 아군이 위험
            if (ctx.EnemiesInMeleeRange < 2) return null;

            // 도발 능력 찾기
            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!IsTauntAbility(ability)) continue;

                // 게임 API로 사용 가능 여부 확인
                string reason;
                var target = new TargetWrapper(ctx.Unit); // 도발은 보통 자신 타겟

                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[Tank] Using taunt: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Taunt - multiple enemies nearby");
                }
            }

            return null;
        }

        /// <summary>
        /// 방어 자세 시도 (Defensive Stance, Brace for Impact 등)
        /// 조건: HP 60% 이하 또는 적 3명 이상에게 둘러싸여 있을 때
        /// </summary>
        private ActionDecision TryUseDefensiveStance(ActionContext ctx)
        {
            // 조건: HP 60% 이하 또는 근접 적 3명 이상
            bool needsDefense = ctx.HPPercent <= 60f || ctx.EnemiesInMeleeRange >= 3;
            if (!needsDefense) return null;

            foreach (var ability in ctx.AvailableAbilities)
            {
                // GameAPI의 방어 자세 감지 사용
                if (!GameAPI.IsDefensiveStanceAbility(ability)) continue;

                // 이미 이번 턴에 사용한 스킬은 스킵
                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;
                if (_usedBuffsThisTurn.Contains(abilityId)) continue;

                string reason;
                var target = new TargetWrapper(ctx.Unit);

                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    _usedBuffsThisTurn.Add(abilityId);
                    string why = ctx.HPPercent <= 60f ? "HP low" : $"{ctx.EnemiesInMeleeRange} enemies nearby";
                    Main.Log($"[Tank] Using defensive stance: {ability.Name} ({why})");
                    return ActionDecision.UseAbility(ability, target, $"Defensive stance - {why}");
                }
            }

            return null;
        }

        /// <summary>
        /// 방어 버프 시도
        /// </summary>
        private ActionDecision TryUseDefensiveBuff(ActionContext ctx)
        {
            // HP가 낮거나 전투 초반
            if (ctx.HPPercent > 80f) return null;

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!IsDefensiveBuff(ability)) continue;

                string reason;
                var target = new TargetWrapper(ctx.Unit);

                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[Tank] Using defensive buff: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Defensive buff - HP is low");
                }
            }

            return null;
        }

        /// <summary>
        /// 근접 공격 시도
        /// </summary>
        private ActionDecision TryMeleeAttack(ActionContext ctx)
        {
            // 근접 능력 찾기 (단일 타겟 우선)
            var meleeAbilities = ctx.AvailableAbilities
                .Where(a => GameAPI.IsMeleeAbility(a))
                .OrderBy(a => CombatHelpers.IsAoEAbility(a) ? 1 : 0)
                .ToList();

            if (meleeAbilities.Count == 0) return null;

            // 가장 가까운 적 타겟
            var target = ctx.NearestEnemy;
            if (target == null) return null;

            var targetWrapper = new TargetWrapper(target);

            foreach (var ability in meleeAbilities)
            {
                // AoE 안전성 체크
                if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, target, ctx.Allies))
                {
                    Main.LogDebug($"[Tank] Skipping {ability.Name} - AoE unsafe");
                    continue;
                }

                // ★ HP 소모 스킬 안전 체크
                if (!IsSafeToUseHPCostAbility(ctx, ability))
                {
                    continue;
                }

                // ★ Veil 안전성 체크 (사이킥 능력인 경우)
                if (!IsSafeToUsePsychicAbility(ability))
                {
                    continue;
                }

                // 게임 API로 사용 가능 여부 확인
                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[Tank] Melee attack: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Melee attack on {target.CharacterName}");
                }
                else
                {
                    Main.LogDebug($"[Tank] Cannot use {ability.Name}: {reason}");
                }
            }

            return null;
        }

        /// <summary>
        /// 원거리 공격 시도 (최후 수단)
        /// </summary>
        private ActionDecision TryRangedAttack(ActionContext ctx)
        {
            var rangedAbilities = ctx.AvailableAbilities
                .Where(a => GameAPI.IsRangedAbility(a))
                .OrderBy(a => CombatHelpers.IsAoEAbility(a) ? 1 : 0)
                .ToList();

            if (rangedAbilities.Count == 0) return null;

            var target = ctx.NearestEnemy;
            if (target == null) return null;

            var targetWrapper = new TargetWrapper(target);

            foreach (var ability in rangedAbilities)
            {
                // AoE 안전성 체크
                if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, target, ctx.Allies))
                {
                    continue;
                }

                // ★ HP 소모 스킬 안전 체크
                if (!IsSafeToUseHPCostAbility(ctx, ability))
                {
                    continue;
                }

                // ★ Veil 안전성 체크 (사이킥 능력인 경우)
                if (!IsSafeToUsePsychicAbility(ability))
                {
                    continue;
                }

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[Tank] Ranged attack (fallback): {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Ranged attack (no melee option)");
                }
            }

            return null;
        }

        /// <summary>
        /// 자기 버프 사용 (공격 후 남은 AP로)
        /// </summary>
        private ActionDecision TryUseSelfBuff(ActionContext ctx)
        {
            foreach (var ability in ctx.AvailableAbilities)
            {
                // 자기 자신 대상 버프만
                if (!GameAPI.IsSelfTargetAbility(ability)) continue;

                // 공격 능력 제외
                if (GameAPI.IsMeleeAbility(ability) || GameAPI.IsRangedAbility(ability)) continue;

                // 무기에서 나온 능력 제외
                if (ability.Weapon != null) continue;

                // ★ 이미 이번 턴에 사용한 버프는 스킵 (반복 방지)
                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;
                if (_usedBuffsThisTurn.Contains(abilityId))
                {
                    Main.LogDebug($"[Tank] Skipping already used buff: {ability.Name}");
                    continue;
                }

                string reason;
                var target = new TargetWrapper(ctx.Unit);

                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    // 사용한 버프 기록
                    _usedBuffsThisTurn.Add(abilityId);
                    Main.Log($"[Tank] Using self buff: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Self buff - no attack available");
                }
            }

            return null;
        }

        #region Ability Classification

        private bool IsTauntAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            return name.Contains("taunt") || name.Contains("provoke") ||
                   name.Contains("도발") || name.Contains("어그로") ||
                   bpName.Contains("taunt") || bpName.Contains("provoke");
        }

        private bool IsDefensiveBuff(AbilityData ability)
        {
            if (ability == null) return false;

            // 자신 타겟 + 아군만 타겟 가능
            if (!GameAPI.IsSelfTargetAbility(ability) && !GameAPI.IsSupportAbility(ability))
                return false;

            string name = ability.Name?.ToLower() ?? "";
            return name.Contains("defend") || name.Contains("protect") ||
                   name.Contains("shield") || name.Contains("armor") ||
                   name.Contains("방어") || name.Contains("보호");
        }

        /// <summary>
        /// HP 소모 스킬을 안전하게 사용할 수 있는지 확인
        /// HP가 임계값(40%) 이하면 사용 불가
        /// </summary>
        private bool IsSafeToUseHPCostAbility(ActionContext ctx, AbilityData ability)
        {
            if (!GameAPI.IsHPCostAbility(ability))
            {
                return true; // HP 소모 스킬이 아니면 항상 OK
            }

            if (ctx.HPPercent <= HP_COST_ABILITY_THRESHOLD)
            {
                Main.Log($"[Tank] HP cost ability {ability.Name} blocked - HP too low ({ctx.HPPercent:F0}% <= {HP_COST_ABILITY_THRESHOLD}%)");
                return false;
            }

            Main.LogDebug($"[Tank] HP cost ability {ability.Name} allowed - HP={ctx.HPPercent:F0}%");
            return true;
        }

        /// <summary>
        /// 사이킥 능력을 안전하게 사용할 수 있는지 확인
        /// Veil 10-14: Major 사이킥 주의 (사용은 가능하지만 로그)
        /// Veil 15+: Major 사이킥 완전 차단
        /// </summary>
        private bool IsSafeToUsePsychicAbility(AbilityData ability)
        {
            var safetyLevel = GameAPI.EvaluatePsychicSafety(ability);

            switch (safetyLevel)
            {
                case PsychicSafetyLevel.Safe:
                    return true;

                case PsychicSafetyLevel.Caution:
                    // Veil 10-14에서 Major 사용: 경고 로그하고 허용
                    Main.Log($"[Tank] CAUTION: Using Major psychic {ability.Name} at {GameAPI.GetVeilStatusString()}");
                    return true;

                case PsychicSafetyLevel.Dangerous:
                    // 사용 후 Veil 15+ 도달: 차단
                    Main.Log($"[Tank] BLOCKED: {ability.Name} would push Veil to DANGER zone ({GameAPI.GetCurrentVeil()}+{GameAPI.GetVeilIncrease(ability)}>=15)");
                    return false;

                case PsychicSafetyLevel.Blocked:
                    // 이미 Veil 15+ 상태에서 Major: 완전 차단
                    Main.Log($"[Tank] BLOCKED: {ability.Name} - Veil already at DANGER level ({GameAPI.GetVeilStatusString()})");
                    return false;

                default:
                    return true;
            }
        }

        #endregion
    }
}
