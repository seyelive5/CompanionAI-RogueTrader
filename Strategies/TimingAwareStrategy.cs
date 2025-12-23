using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2_2.Core;

namespace CompanionAI_v2_2.Strategies
{
    /// <summary>
    /// v2.2.0: 타이밍 인식 전략 기본 클래스
    ///
    /// 모든 역할 전략의 기반
    /// 공통 타이밍 로직과 안전성 체크 제공
    /// </summary>
    public abstract class TimingAwareStrategy : IUnitStrategy
    {
        public abstract string StrategyName { get; }

        // HP 소모 스킬 안전 임계값
        protected const float HP_COST_THRESHOLD = 40f;

        public abstract ActionDecision DecideAction(ActionContext ctx);

        #region Timing Phase Methods

        /// <summary>
        /// Phase 1: 긴급 자기 힐 (HP < 30%)
        /// </summary>
        protected ActionDecision TryEmergencySelfHeal(ActionContext ctx)
        {
            if (ctx.HPPercent >= 30f) return null;

            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!IsHealAbility(ability)) continue;
                if (!GameAPI.IsSelfTargetAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[{StrategyName}] Emergency heal: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Emergency self-heal");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 2: 선제적 버프 (PreCombatBuff, PreAttackBuff)
        /// 첫 행동 전에만 사용
        /// </summary>
        protected ActionDecision TryProactiveBuffs(ActionContext ctx)
        {
            // 이미 첫 행동을 했으면 스킵
            if (ctx.HasPerformedFirstAction) return null;

            var proactiveBuffs = GameAPI.FilterProactiveBuffs(ctx.AvailableAbilities, ctx.Unit);
            if (proactiveBuffs.Count == 0) return null;

            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in proactiveBuffs)
            {
                // PostFirstAction 스킬은 여기서 제외
                if (AbilityRulesDatabase.IsPostFirstAction(ability)) continue;

                // 타이밍 체크
                if (!GameAPI.CanUseAbilityAtCurrentTiming(ability, ctx)) continue;

                // 사이킥 안전성
                if (!IsSafePsychicAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[{StrategyName}] Proactive buff: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Proactive buff");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 3: 디버프 적용 (첫 공격 전)
        /// </summary>
        protected ActionDecision TryDebuffs(ActionContext ctx, BaseUnitEntity target)
        {
            if (target == null) return null;

            var targetWrapper = new TargetWrapper(target);

            foreach (var ability in ctx.AvailableAbilities)
            {
                var timing = AbilityRulesDatabase.GetTiming(ability);
                if (timing != AbilityTiming.Debuff) continue;

                // 이미 적용된 디버프인지 체크
                if (GameAPI.HasActiveBuffOnTarget(target, ability)) continue;

                if (!IsSafePsychicAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[{StrategyName}] Apply debuff: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Debuff on {target.CharacterName}");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 4: 공격 실행
        /// ★ v2.2.1: 상황에 맞는 스마트 공격 선택
        /// </summary>
        protected ActionDecision TryAttack(ActionContext ctx, BaseUnitEntity preferredTarget)
        {
            var target = preferredTarget ?? ctx.NearestEnemy;
            if (target == null) return null;

            // 마무리 스킬 우선 (적 HP 낮은 경우)
            var finisherResult = TryFinisher(ctx, target);
            if (finisherResult != null) return finisherResult;

            // ★ v2.2.1: 스마트 공격 선택 - 상황에 맞는 최적 공격
            var attacks = GetOffensiveAbilities(ctx.AvailableAbilities);

            // 우선순위별로 공격 정렬 (타겟 기준)
            var prioritizedAttacks = attacks
                .Select(a => new {
                    Ability = a,
                    Priority = CombatHelpers.GetAttackPriority(a, ctx.Unit, target, ctx.Enemies, ctx.Allies),
                    Type = CombatHelpers.GetWeaponAttackType(a)
                })
                .OrderBy(x => x.Priority)
                .ToList();

            foreach (var attack in prioritizedAttacks)
            {
                var ability = attack.Ability;

                // 타이밍 체크
                if (!GameAPI.CanUseAbilityAtCurrentTiming(ability, ctx)) continue;

                // AoE 안전성
                if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, target, ctx.Allies)) continue;

                // 수류탄 효율성 체크 (적 2명 이상)
                if (!CombatHelpers.IsGrenadeEfficient(ability, target, ctx.Enemies))
                {
                    Main.LogDebug($"[{StrategyName}] Grenade {ability.Name} skipped - not enough enemies nearby");
                    continue;
                }

                // HP 소모 안전성
                if (!IsSafeHPCostAbility(ctx, ability)) continue;

                // 사이킥 안전성
                if (!IsSafePsychicAbility(ability)) continue;

                var targetWrapper = new TargetWrapper(target);
                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    string attackType = attack.Type.ToString();
                    Main.Log($"[{StrategyName}] Attack ({attackType}): {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Attack {target.CharacterName}");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 5: PostFirstAction 스킬 (Run and Gun 등)
        /// 첫 행동 후에만 사용
        /// </summary>
        protected ActionDecision TryPostFirstAction(ActionContext ctx)
        {
            // 첫 행동을 아직 안 했으면 스킵
            if (!ctx.HasPerformedFirstAction) return null;

            var postActionAbilities = GameAPI.FilterPostFirstActionAbilities(ctx.AvailableAbilities);
            if (postActionAbilities.Count == 0) return null;

            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in postActionAbilities)
            {
                // 타이밍 체크
                if (!GameAPI.CanUseAbilityAtCurrentTiming(ability, ctx)) continue;

                // HP 소모 안전성
                if (!IsSafeHPCostAbility(ctx, ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[{StrategyName}] PostFirstAction: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Post-action ability (Run and Gun, etc.)");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 6: 턴 종료 스킬 (마지막에만)
        /// </summary>
        protected ActionDecision TryTurnEndingAbility(ActionContext ctx)
        {
            // 다른 유효한 공격이 있으면 스킵
            if (HasAnyValidAttack(ctx)) return null;

            var turnEndingAbilities = GameAPI.FilterTurnEndingAbilities(ctx.AvailableAbilities);
            if (turnEndingAbilities.Count == 0) return null;

            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in turnEndingAbilities)
            {
                // HP 소모 안전성
                if (!IsSafeHPCostAbility(ctx, ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[{StrategyName}] Turn ending: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Turn ending ability");
                }
            }

            return null;
        }

        /// <summary>
        /// 마무리 스킬 시도 (Finisher)
        /// </summary>
        protected ActionDecision TryFinisher(ActionContext ctx, BaseUnitEntity target)
        {
            if (target == null) return null;

            var finishers = GameAPI.FilterFinisherAbilities(ctx.AvailableAbilities);
            if (finishers.Count == 0) return null;

            foreach (var ability in finishers)
            {
                var rule = AbilityRulesDatabase.GetRule(ability);
                float threshold = rule?.TargetHPThreshold ?? 30f;

                // 타겟 HP 체크
                if (GameAPI.GetHPPercent(target) > threshold) continue;

                var targetWrapper = new TargetWrapper(target);
                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[{StrategyName}] Finisher: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Finish {target.CharacterName}");
                }
            }

            return null;
        }

        #endregion

        #region Safety Checks

        protected bool IsSafeHPCostAbility(ActionContext ctx, AbilityData ability)
        {
            if (!GameAPI.IsHPCostAbility(ability)) return true;

            var rule = AbilityRulesDatabase.GetRule(ability);
            float threshold = rule?.HPThreshold ?? HP_COST_THRESHOLD;

            if (ctx.HPPercent < threshold)
            {
                Main.LogDebug($"[{StrategyName}] HP cost {ability.Name} blocked - HP too low ({ctx.HPPercent:F0}% < {threshold}%)");
                return false;
            }

            return true;
        }

        protected bool IsSafePsychicAbility(AbilityData ability)
        {
            var safety = GameAPI.EvaluatePsychicSafety(ability);

            switch (safety)
            {
                case PsychicSafetyLevel.Safe:
                case PsychicSafetyLevel.Caution:
                    return true;

                case PsychicSafetyLevel.Dangerous:
                case PsychicSafetyLevel.Blocked:
                    Main.LogDebug($"[{StrategyName}] Psychic {ability.Name} blocked - {safety}");
                    return false;

                default:
                    return true;
            }
        }

        #endregion

        #region Helpers

        protected bool IsHealAbility(AbilityData ability)
        {
            if (ability == null) return false;
            string name = ability.Name?.ToLower() ?? "";
            return name.Contains("heal") || name.Contains("mend") ||
                   name.Contains("치유") || name.Contains("회복");
        }

        protected List<AbilityData> GetOffensiveAbilities(List<AbilityData> abilities)
        {
            // ★ v2.2.1: 정렬은 TryAttack에서 상황에 맞게 수행
            return abilities
                .Where(a => GameAPI.IsOffensiveAbility(a) ||
                           GameAPI.IsMeleeAbility(a) ||
                           GameAPI.IsRangedAbility(a))
                .Where(a => !AbilityRulesDatabase.IsPostFirstAction(a))
                .Where(a => !AbilityRulesDatabase.IsTurnEnding(a))
                .Where(a => !AbilityRulesDatabase.IsFinisher(a))
                .ToList();
        }

        protected bool HasAnyValidAttack(ActionContext ctx)
        {
            var attacks = GetOffensiveAbilities(ctx.AvailableAbilities);

            foreach (var ability in attacks)
            {
                if (!IsSafeHPCostAbility(ctx, ability)) continue;
                if (!IsSafePsychicAbility(ability)) continue;

                foreach (var enemy in ctx.Enemies)
                {
                    if (enemy == null || enemy.LifeState.IsDead) continue;

                    if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, enemy, ctx.Allies)) continue;

                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, new TargetWrapper(enemy), out reason))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        protected ActionDecision TryMoveToEnemy(ActionContext ctx)
        {
            if (!ctx.CanMove) return null;
            if (ctx.NearestEnemy == null) return null;

            // ★ v2.2.1: 이동 대신 사거리 밖 공격 선택 (게임이 자동 이동)
            var moveAttackResult = TryForceAttackIgnoringRange(ctx);
            if (moveAttackResult != null) return moveAttackResult;

            // 폴백: 기존 이동 로직
            if (!HasAnyValidAttack(ctx))
            {
                Main.Log($"[{StrategyName}] Moving to attack range");
                return ActionDecision.Move("Move to attack range");
            }

            return null;
        }

        /// <summary>
        /// ★ v2.2.1: Force Basic Attack 폴백
        /// 모든 스킬이 사용 불가할 때 기본 무기 공격으로 폴백
        /// </summary>
        protected ActionDecision TryForceBasicAttack(ActionContext ctx)
        {
            // 적이 없으면 스킵
            if (ctx.Enemies.Count == 0) return null;

            // ★ v2.2.2: 디버그 - 사용 가능한 능력 분석
            var allWeaponAbilities = ctx.AvailableAbilities.Where(a => a.Weapon != null).ToList();
            Main.LogDebug($"[{StrategyName}] Available abilities: {ctx.AvailableAbilities.Count}, Weapon abilities: {allWeaponAbilities.Count}");

            if (allWeaponAbilities.Count == 0)
            {
                // 무기 능력이 전혀 없으면 모든 능력 이름 로깅
                var abilityNames = string.Join(", ", ctx.AvailableAbilities.Take(5).Select(a => a.Name));
                Main.LogDebug($"[{StrategyName}] No weapon abilities! Sample: {abilityNames}");
            }

            // 무기 기반 공격 찾기 (PostFirstAction, TurnEnding, 수류탄 제외)
            var basicAttacks = ctx.AvailableAbilities
                .Where(a => a.Weapon != null)
                .Where(a => !AbilityRulesDatabase.IsPostFirstAction(a))
                .Where(a => !AbilityRulesDatabase.IsTurnEnding(a))
                .Where(a => !CombatHelpers.IsGrenadeOrExplosive(a)) // ★ 수류탄 제외
                .ToList();

            if (basicAttacks.Count == 0)
            {
                Main.LogDebug($"[{StrategyName}] No basic attack available (after filtering)");
                return null;
            }

            // 타겟 찾기 - 가장 가까운 적 우선
            var targets = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetDistance(ctx.Unit, e))
                .ToList();

            // ★ v2.2.2: 각 적에 대한 실패 이유 수집
            var failReasons = new List<string>();

            foreach (var basicAttack in basicAttacks)
            {
                foreach (var enemy in targets)
                {
                    var targetWrapper = new TargetWrapper(enemy);
                    string reason;
                    if (GameAPI.CanUseAbilityOn(basicAttack, targetWrapper, out reason))
                    {
                        Main.Log($"[{StrategyName}] ★ FORCE BASIC ATTACK: {basicAttack.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(basicAttack, targetWrapper,
                            $"Force basic attack on {enemy.CharacterName}");
                    }
                    else
                    {
                        failReasons.Add($"{enemy.CharacterName}:{reason}");
                    }
                }
            }

            // 실패 이유 로깅 (최대 3개)
            var sampleReasons = string.Join(", ", failReasons.Take(3));
            Main.LogDebug($"[{StrategyName}] Force basic attack failed - {sampleReasons}");
            return null;
        }

        /// <summary>
        /// ★ v2.2.1: 사거리 무시하고 공격 선택 (게임이 자동으로 이동 후 공격)
        /// ★ v2.2.2: 더 적극적으로 - LoS 문제도 무시하고 공격 시도
        /// </summary>
        protected ActionDecision TryForceAttackIgnoringRange(ActionContext ctx)
        {
            if (ctx.Enemies.Count == 0) return null;
            if (!ctx.CanMove) return null; // 이동 불가면 의미 없음

            // 무기 기반 공격 찾기
            var basicAttacks = ctx.AvailableAbilities
                .Where(a => a.Weapon != null)
                .Where(a => !AbilityRulesDatabase.IsPostFirstAction(a))
                .Where(a => !AbilityRulesDatabase.IsTurnEnding(a))
                .Where(a => !CombatHelpers.IsGrenadeOrExplosive(a))
                .ToList();

            if (basicAttacks.Count == 0) return null;

            // 모든 적 시도 (가까운 순)
            var enemies = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetDistance(ctx.Unit, e))
                .ToList();

            // ★ v2.2.3: LoS 문제 있어도 사용할 폴백 저장
            AbilityData fallbackAttack = null;
            BaseUnitEntity fallbackEnemy = null;

            foreach (var enemy in enemies)
            {
                var targetWrapper = new TargetWrapper(enemy);
                float distance = GameAPI.GetDistance(ctx.Unit, enemy);

                foreach (var attack in basicAttacks)
                {
                    string reason;
                    bool canUse = GameAPI.CanUseAbilityOn(attack, targetWrapper, out reason);

                    if (canUse)
                    {
                        // 사용 가능하면 바로 사용
                        Main.Log($"[{StrategyName}] Attack: {attack.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(attack, targetWrapper, $"Attack {enemy.CharacterName}");
                    }
                    else
                    {
                        string reasonLower = reason?.ToLower() ?? "";
                        bool isLoSIssue = reasonLower.Contains("los") || reasonLower.Contains("sight") ||
                                          reasonLower.Contains("visible") || reasonLower.Contains("see");

                        // ★ v2.2.3: 첫 번째 폴백 저장 (LoS 문제든 사거리 문제든)
                        if (fallbackAttack == null)
                        {
                            fallbackAttack = attack;
                            fallbackEnemy = enemy;
                        }

                        if (isLoSIssue)
                        {
                            Main.LogDebug($"[{StrategyName}] {enemy.CharacterName} has LoS issue - trying others first");
                            break; // 다른 적 먼저 시도
                        }

                        // 사거리 문제면 이동+공격 시도
                        if (distance > 3f)
                        {
                            Main.Log($"[{StrategyName}] ★ ATTACK WITH MOVE: {attack.Name} -> {enemy.CharacterName} (dist={distance:F1}m, reason={reason})");
                            return ActionDecision.UseAbility(attack, targetWrapper,
                                $"Attack with move on {enemy.CharacterName}");
                        }
                    }
                }
            }

            // ★ v2.2.3: 모든 적이 LoS 문제여도 폴백 공격 시도 (게임이 경로 찾기 시도)
            if (fallbackAttack != null && fallbackEnemy != null)
            {
                var targetWrapper = new TargetWrapper(fallbackEnemy);
                Main.Log($"[{StrategyName}] ★ FORCE ATTACK (LoS bypass): {fallbackAttack.Name} -> {fallbackEnemy.CharacterName}");
                return ActionDecision.UseAbility(fallbackAttack, targetWrapper,
                    $"Force attack on {fallbackEnemy.CharacterName} (LoS bypass)");
            }

            return null;
        }

        #endregion
    }
}
