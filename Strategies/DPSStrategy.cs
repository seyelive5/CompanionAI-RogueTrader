using System.Collections.Generic;
using System.Linq;
using CompanionAI_v2.Core;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;

namespace CompanionAI_v2.Strategies
{
    /// <summary>
    /// v2.1.0: DPS 전략 - 우선순위 기반 결정
    ///
    /// 우선순위:
    /// 1. 공격 버프 (전투 초반)
    /// 2. 마무리 공격 (HP 낮은 적)
    /// 3. 최고 데미지 공격
    /// 4. 이동 (사거리 확보)
    /// </summary>
    public class DPSStrategy : IUnitStrategy
    {
        public string StrategyName => "DPS";

        // HP 소모 스킬 안전 임계값 (이 HP% 이하면 HP 소모 스킬 사용 안함)
        private const float HP_COST_ABILITY_THRESHOLD = 40f;

        // 이번 턴에 사용한 버프 추적 (반복 방지)
        private static HashSet<string> _usedAbilitiesThisTurn = new HashSet<string>();
        private static string _lastUnitId = null;

        public ActionDecision DecideAction(ActionContext ctx)
        {
            // 유닛이 바뀌면 사용 능력 추적 초기화
            string unitId = ctx.Unit.UniqueId;
            if (_lastUnitId != unitId)
            {
                _usedAbilitiesThisTurn.Clear();
                _lastUnitId = unitId;
            }

            // ★ Veil 및 Momentum 상태 로깅
            Main.Log($"[DPS] {ctx.Unit.CharacterName}: HP={ctx.HPPercent:F0}%, {GameAPI.GetVeilStatusString()}, {GameAPI.GetMomentumStatusString()}, Nearest={ctx.NearestEnemy?.CharacterName}, Dist={ctx.NearestEnemyDistance:F1}");

            // 설정에서 근접/원거리 선호도 확인
            bool preferMelee = ctx.Settings?.RangePreference == Settings.RangePreference.PreferMelee;

            // 0. Righteous Fury / Revel in Slaughter (사용 가능하면 즉시 사용)
            var furyResult = TryUseRighteousFury(ctx);
            if (furyResult != null) return furyResult;

            // 1. 공격 버프 (전투 초반에 한 번)
            var buffResult = TryUseOffensiveBuff(ctx);
            if (buffResult != null) return buffResult;

            // 2. Heroic Act (Momentum 175+ 일 때)
            if (GameAPI.IsHeroicActAvailable())
            {
                var heroicResult = TryUseHeroicAct(ctx);
                if (heroicResult != null) return heroicResult;
            }

            // 3. 마무리 공격 (HP 낮은 적 우선 처리)
            var finishResult = TryFinishLowHPEnemy(ctx, preferMelee);
            if (finishResult != null) return finishResult;

            // 4. 주요 공격
            var attackResult = TryBestAttack(ctx, preferMelee);
            if (attackResult != null) return attackResult;

            // 5. 어떤 공격이라도 (AoE 무시하고 단일 타겟 시도)
            var fallbackResult = TryAnyAttack(ctx);
            if (fallbackResult != null) return fallbackResult;

            // 6. 기본 무기 공격 강제 시도 (모든 안전 체크 무시)
            var forceAttackResult = TryForceBasicAttack(ctx);
            if (forceAttackResult != null) return forceAttackResult;

            // 7. 이동이 필요한가? (공격이 모두 실패한 경우에만)
            if (ctx.CanMove && ctx.NearestEnemy != null)
            {
                // 이미 근접 범위 근처(5m 이내)면 이동 불필요
                if (ctx.NearestEnemyDistance > 5f)
                {
                    // 근접 선호인데 멀리 있으면 이동
                    if (preferMelee && !ctx.IsInMeleeRange)
                    {
                        return ActionDecision.Move("Moving to melee range");
                    }

                    // 원거리인데 사거리 밖이면 이동
                    if (!preferMelee && !HasAnyAttackInRange(ctx))
                    {
                        return ActionDecision.Move("Moving to attack range");
                    }
                }
            }

            return ActionDecision.EndTurn("No valid action available");
        }

        /// <summary>
        /// 공격 버프 시도
        /// </summary>
        private ActionDecision TryUseOffensiveBuff(ActionContext ctx)
        {
            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!IsOffensiveBuff(ability)) continue;

                // ★ 이미 이번 턴에 사용한 버프는 스킵 (반복 방지)
                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;
                if (_usedAbilitiesThisTurn.Contains(abilityId))
                {
                    Main.LogDebug($"[DPS] Skipping already used buff: {ability.Name}");
                    continue;
                }

                string reason;
                var target = new TargetWrapper(ctx.Unit);

                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    // 사용한 버프 기록
                    _usedAbilitiesThisTurn.Add(abilityId);
                    Main.Log($"[DPS] Using offensive buff: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Offensive buff before attack");
                }
            }

            return null;
        }

        /// <summary>
        /// Righteous Fury / Revel in Slaughter 사용 시도
        /// 적 3명 처치 후 활성화되는 강력한 버프
        /// </summary>
        private ActionDecision TryUseRighteousFury(ActionContext ctx)
        {
            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsRighteousFuryAbility(ability)) continue;

                // 이미 사용한 스킬은 스킵
                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;
                if (_usedAbilitiesThisTurn.Contains(abilityId)) continue;

                string reason;
                var target = new TargetWrapper(ctx.Unit);

                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    _usedAbilitiesThisTurn.Add(abilityId);
                    Main.Log($"[DPS] RIGHTEOUS FURY: {ability.Name} is available! Using immediately.");
                    return ActionDecision.UseAbility(ability, target, "Righteous Fury - maximum damage buff");
                }
            }

            return null;
        }

        /// <summary>
        /// Heroic Act 사용 시도 (Momentum 175+ 필요)
        /// </summary>
        private ActionDecision TryUseHeroicAct(ActionContext ctx)
        {
            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsHeroicActAbility(ability)) continue;

                // 이미 사용한 스킬은 스킵
                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;
                if (_usedAbilitiesThisTurn.Contains(abilityId)) continue;

                string reason;
                var target = new TargetWrapper(ctx.Unit);

                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    _usedAbilitiesThisTurn.Add(abilityId);
                    Main.Log($"[DPS] HEROIC ACT: {ability.Name} - Momentum is {GameAPI.GetCurrentMomentum()}, using heroic ability!");
                    return ActionDecision.UseAbility(ability, target, "Heroic Act - high momentum");
                }
            }

            return null;
        }

        /// <summary>
        /// HP 낮은 적 마무리 시도
        /// </summary>
        private ActionDecision TryFinishLowHPEnemy(ActionContext ctx, bool preferMelee)
        {
            // HP 30% 이하인 적 찾기
            var weakEnemy = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead && GameAPI.GetHPPercent(e) < 30f)
                .OrderBy(e => GameAPI.GetHPPercent(e))
                .FirstOrDefault();

            if (weakEnemy == null) return null;

            var targetWrapper = new TargetWrapper(weakEnemy);

            // 적합한 공격 능력 찾기 - 단일 타겟 우선
            var attackAbilities = GetAttackAbilities(ctx, preferMelee)
                .OrderBy(a => CombatHelpers.IsAoEAbility(a) ? 1 : 0)
                .ToList();

            foreach (var ability in attackAbilities)
            {
                // AoE 안전성 체크
                if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, weakEnemy, ctx.Allies))
                {
                    Main.LogDebug($"[DPS] Skipping {ability.Name} - AoE unsafe");
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
                    Main.Log($"[DPS] Finishing low HP enemy: {ability.Name} -> {weakEnemy.CharacterName} ({GameAPI.GetHPPercent(weakEnemy):F0}%)");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Finish {weakEnemy.CharacterName}");
                }
            }

            return null;
        }

        /// <summary>
        /// 최고 데미지 공격 시도
        /// </summary>
        private ActionDecision TryBestAttack(ActionContext ctx, bool preferMelee)
        {
            var target = ctx.NearestEnemy;
            if (target == null) return null;

            var targetWrapper = new TargetWrapper(target);

            // 단일 타겟 우선 정렬
            var attackAbilities = GetAttackAbilities(ctx, preferMelee)
                .OrderBy(a => CombatHelpers.IsAoEAbility(a) ? 1 : 0)
                .ToList();

            foreach (var ability in attackAbilities)
            {
                // AoE 안전성 체크
                if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, target, ctx.Allies))
                {
                    Main.LogDebug($"[DPS] Skipping {ability.Name} - AoE unsafe");
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
                    Main.Log($"[DPS] Best attack: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Attack {target.CharacterName}");
                }
                else
                {
                    Main.LogDebug($"[DPS] Cannot use {ability.Name}: {reason}");
                }
            }

            return null;
        }

        /// <summary>
        /// 아무 공격이라도 시도
        /// </summary>
        private ActionDecision TryAnyAttack(ActionContext ctx)
        {
            var target = ctx.NearestEnemy;
            if (target == null) return null;

            var targetWrapper = new TargetWrapper(target);

            // 단일 타겟 우선
            var abilities = ctx.AvailableAbilities
                .Where(a => GameAPI.IsOffensiveAbility(a) || GameAPI.IsMeleeAbility(a) || GameAPI.IsRangedAbility(a))
                .OrderBy(a => CombatHelpers.IsAoEAbility(a) ? 1 : 0)
                .ToList();

            foreach (var ability in abilities)
            {
                // AoE 안전성 체크
                if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, target, ctx.Allies))
                {
                    Main.LogDebug($"[DPS] Fallback skipping {ability.Name} - AoE unsafe");
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
                    Main.Log($"[DPS] Fallback attack: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Fallback attack");
                }
            }

            return null;
        }

        /// <summary>
        /// 기본 무기 공격 강제 시도 - 모든 안전 체크 무시 (최후의 수단)
        /// AoE 안전성, HP 소모 체크를 무시하고 기본 공격 시도
        /// </summary>
        private ActionDecision TryForceBasicAttack(ActionContext ctx)
        {
            var target = ctx.NearestEnemy;
            if (target == null) return null;

            var targetWrapper = new TargetWrapper(target);

            // 무기에서 나온 단일 타겟 공격만 시도 (AoE 제외)
            var weaponAttacks = ctx.AvailableAbilities
                .Where(a => a.Weapon != null && !CombatHelpers.IsAoEAbility(a))
                .ToList();

            foreach (var ability in weaponAttacks)
            {
                // Veil 체크만 유지 (Perils of the Warp는 치명적)
                if (!IsSafeToUsePsychicAbility(ability))
                {
                    continue;
                }

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[DPS] FORCE ATTACK: {ability.Name} -> {target.CharacterName} (ignoring safety checks)");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Force attack on {target.CharacterName}");
                }
                else
                {
                    Main.LogDebug($"[DPS] Force attack {ability.Name} failed: {reason}");
                }
            }

            // 무기 공격도 실패하면 모든 단일 타겟 공격 시도
            var singleTargetAttacks = ctx.AvailableAbilities
                .Where(a => (GameAPI.IsOffensiveAbility(a) || GameAPI.IsMeleeAbility(a) || GameAPI.IsRangedAbility(a))
                           && !CombatHelpers.IsAoEAbility(a))
                .ToList();

            foreach (var ability in singleTargetAttacks)
            {
                // Veil 체크만 유지
                if (!IsSafeToUsePsychicAbility(ability))
                {
                    continue;
                }

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[DPS] FORCE ATTACK (single): {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Force single attack");
                }
            }

            Main.Log($"[DPS] No valid attack available for {ctx.Unit.CharacterName} - all options exhausted");
            return null;
        }

        #region Helper Methods

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
                    Main.Log($"[DPS] CAUTION: Using Major psychic {ability.Name} at {GameAPI.GetVeilStatusString()}");
                    return true;

                case PsychicSafetyLevel.Dangerous:
                    // 사용 후 Veil 15+ 도달: 차단
                    Main.Log($"[DPS] BLOCKED: {ability.Name} would push Veil to DANGER zone ({GameAPI.GetCurrentVeil()}+{GameAPI.GetVeilIncrease(ability)}>=15)");
                    return false;

                case PsychicSafetyLevel.Blocked:
                    // 이미 Veil 15+ 상태에서 Major: 완전 차단
                    Main.Log($"[DPS] BLOCKED: {ability.Name} - Veil already at DANGER level ({GameAPI.GetVeilStatusString()})");
                    return false;

                default:
                    return true;
            }
        }

        private List<AbilityData> GetAttackAbilities(ActionContext ctx, bool preferMelee)
        {
            var attacks = ctx.AvailableAbilities
                .Where(a => GameAPI.IsOffensiveAbility(a) || GameAPI.IsMeleeAbility(a) || GameAPI.IsRangedAbility(a))
                .ToList();

            if (preferMelee)
            {
                // 근접 우선 정렬
                return attacks.OrderByDescending(a => GameAPI.IsMeleeAbility(a) ? 1 : 0).ToList();
            }
            else
            {
                // 원거리 우선 정렬
                return attacks.OrderByDescending(a => GameAPI.IsRangedAbility(a) ? 1 : 0).ToList();
            }
        }

        private bool HasAnyAttackInRange(ActionContext ctx)
        {
            var target = ctx.NearestEnemy;
            if (target == null) return false;

            var targetWrapper = new TargetWrapper(target);

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsOffensiveAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsOffensiveBuff(AbilityData ability)
        {
            if (ability == null) return false;
            if (!GameAPI.IsSelfTargetAbility(ability)) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // 공격 관련 버프 키워드
            return name.Contains("rage") || name.Contains("fury") ||
                   name.Contains("berserk") || name.Contains("charge") ||
                   name.Contains("준비") || name.Contains("집중") ||
                   bpName.Contains("preattack") || bpName.Contains("buff");
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
                Main.Log($"[DPS] HP cost ability {ability.Name} blocked - HP too low ({ctx.HPPercent:F0}% <= {HP_COST_ABILITY_THRESHOLD}%)");
                return false;
            }

            Main.LogDebug($"[DPS] HP cost ability {ability.Name} allowed - HP={ctx.HPPercent:F0}%");
            return true;
        }

        #endregion
    }
}
