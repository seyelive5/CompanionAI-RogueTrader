using System.Collections.Generic;
using System.Linq;
using CompanionAI_v2.Core;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;

namespace CompanionAI_v2.Strategies
{
    /// <summary>
    /// v2.1.0: Balanced 전략 - 상황에 따라 유연하게 대응
    ///
    /// 우선순위:
    /// 1. 긴급 상황 대응 (자신 HP 낮음, 아군 위험)
    /// 2. 버프 (전투 초반)
    /// 3. 공격 (가장 효율적인 대상)
    /// 4. 이동 (필요 시)
    /// </summary>
    public class BalancedStrategy : IUnitStrategy
    {
        public string StrategyName => "Balanced";

        // HP 소모 스킬 안전 임계값 (이 HP% 이하면 HP 소모 스킬 사용 안함)
        private const float HP_COST_ABILITY_THRESHOLD = 40f;

        public ActionDecision DecideAction(ActionContext ctx)
        {
            // ★ Veil 및 Momentum 상태 로깅
            Main.Log($"[Balanced] {ctx.Unit.CharacterName}: HP={ctx.HPPercent:F0}%, {GameAPI.GetVeilStatusString()}, {GameAPI.GetMomentumStatusString()}, Enemies={ctx.Enemies.Count}, InMelee={ctx.IsInMeleeRange}");

            // 1. 긴급 상황: 자신 HP 낮음
            if (ctx.HPPercent < 30f)
            {
                var healResult = TrySelfHeal(ctx);
                if (healResult != null) return healResult;
            }

            // 2. 버프 사용 (사용 가능한 버프가 있으면)
            var buffResult = TryUseBuff(ctx);
            if (buffResult != null) return buffResult;

            // 3. 가장 적합한 공격
            var attackResult = TryBestAttack(ctx);
            if (attackResult != null) return attackResult;

            // 4. 다른 적에게라도 공격 시도 (AoE가 차단된 경우 대비)
            var fallbackAttack = TryAttackAnyEnemy(ctx);
            if (fallbackAttack != null) return fallbackAttack;

            // 5. 이동 필요 시
            if (ctx.CanMove && ctx.NearestEnemy != null && !HasAnySafeAttackInRange(ctx))
            {
                return ActionDecision.Move("Moving to attack range");
            }

            return ActionDecision.EndTurn("No valid action");
        }

        private ActionDecision TrySelfHeal(ActionContext ctx)
        {
            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!IsHealAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[Balanced] Self heal: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Self heal - HP critical");
                }
            }

            return null;
        }

        private ActionDecision TryUseBuff(ActionContext ctx)
        {
            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsSelfTargetAbility(ability)) continue;
                if (!GameAPI.IsSupportAbility(ability) && !IsSelfBuff(ability)) continue;
                if (IsHealAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[Balanced] Using buff: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Using buff");
                }
            }

            return null;
        }

        private ActionDecision TryBestAttack(ActionContext ctx)
        {
            // 무기 타입에 따라 근접/원거리 결정
            bool preferMelee = ctx.HasMeleeWeapon && ctx.IsInMeleeRange;

            var target = preferMelee ? ctx.NearestEnemy : (ctx.WeakestEnemy ?? ctx.NearestEnemy);
            if (target == null) return null;

            var targetWrapper = new TargetWrapper(target);

            // 공격 능력 정렬 - 단일 타겟 우선, 근접/원거리 선호도 적용
            var attacks = ctx.AvailableAbilities
                .Where(a => GameAPI.IsOffensiveAbility(a) || GameAPI.IsMeleeAbility(a) || GameAPI.IsRangedAbility(a))
                .OrderBy(a => CombatHelpers.IsAoEAbility(a) ? 1 : 0)  // 단일 타겟 우선
                .ThenByDescending(a => preferMelee ? (GameAPI.IsMeleeAbility(a) ? 1 : 0) : (GameAPI.IsRangedAbility(a) ? 1 : 0))
                .ToList();

            foreach (var ability in attacks)
            {
                // AoE 안전성 체크
                if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, target, ctx.Allies))
                {
                    Main.LogDebug($"[Balanced] Skipping {ability.Name} - AoE unsafe");
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
                    Main.Log($"[Balanced] Attack: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Attack {target.CharacterName}");
                }
                else
                {
                    Main.LogDebug($"[Balanced] Cannot use {ability.Name}: {reason}");
                }
            }

            return null;
        }

        /// <summary>
        /// 아무 적에게라도 공격 시도 - AoE가 차단된 경우 다른 적 시도
        /// </summary>
        private ActionDecision TryAttackAnyEnemy(ActionContext ctx)
        {
            // 모든 적에 대해 시도
            var enemies = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetDistance(ctx.Unit, e))
                .ToList();

            // 단일 타겟 능력 우선
            var attacks = ctx.AvailableAbilities
                .Where(a => GameAPI.IsOffensiveAbility(a) || GameAPI.IsMeleeAbility(a) || GameAPI.IsRangedAbility(a))
                .OrderBy(a => CombatHelpers.IsAoEAbility(a) ? 1 : 0)
                .ToList();

            foreach (var ability in attacks)
            {
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

                foreach (var enemy in enemies)
                {
                    // AoE 안전성 체크
                    if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, enemy, ctx.Allies))
                    {
                        continue;
                    }

                    var targetWrapper = new TargetWrapper(enemy);
                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        Main.Log($"[Balanced] Fallback attack: {ability.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Fallback attack on {enemy.CharacterName}");
                    }
                }
            }

            return null;
        }

        private bool HasAnySafeAttackInRange(ActionContext ctx)
        {
            var enemies = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .ToList();

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsOffensiveAbility(ability) && !GameAPI.IsMeleeAbility(ability) && !GameAPI.IsRangedAbility(ability))
                    continue;

                foreach (var enemy in enemies)
                {
                    // AoE 안전성 체크
                    if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, enemy, ctx.Allies))
                        continue;

                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, new TargetWrapper(enemy), out reason))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool IsHealAbility(AbilityData ability)
        {
            if (ability == null) return false;
            string name = ability.Name?.ToLower() ?? "";
            return name.Contains("heal") || name.Contains("치유") || name.Contains("회복");
        }

        private bool IsSelfBuff(AbilityData ability)
        {
            if (ability == null) return false;
            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            return bpName.Contains("buff") || bpName.Contains("preattack") ||
                   name.Contains("준비") || name.Contains("집중");
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
                Main.Log($"[Balanced] HP cost ability {ability.Name} blocked - HP too low ({ctx.HPPercent:F0}% <= {HP_COST_ABILITY_THRESHOLD}%)");
                return false;
            }

            Main.LogDebug($"[Balanced] HP cost ability {ability.Name} allowed - HP={ctx.HPPercent:F0}%");
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
                    Main.Log($"[Balanced] CAUTION: Using Major psychic {ability.Name} at {GameAPI.GetVeilStatusString()}");
                    return true;

                case PsychicSafetyLevel.Dangerous:
                    // 사용 후 Veil 15+ 도달: 차단
                    Main.Log($"[Balanced] BLOCKED: {ability.Name} would push Veil to DANGER zone ({GameAPI.GetCurrentVeil()}+{GameAPI.GetVeilIncrease(ability)}>=15)");
                    return false;

                case PsychicSafetyLevel.Blocked:
                    // 이미 Veil 15+ 상태에서 Major: 완전 차단
                    Main.Log($"[Balanced] BLOCKED: {ability.Name} - Veil already at DANGER level ({GameAPI.GetVeilStatusString()})");
                    return false;

                default:
                    return true;
            }
        }
    }
}
