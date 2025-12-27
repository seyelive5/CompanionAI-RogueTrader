using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2_2.Core;
using static CompanionAI_v2_2.Core.AbilityDatabase;

namespace CompanionAI_v2_2.Strategies
{
    /// <summary>
    /// v2.2.0: DPS 전략 - 타이밍 인식 딜러형
    /// ★ v2.2.28: AbilityUsageTracker로 중앙화된 추적 시스템 사용
    ///
    /// 특징:
    /// - 약한 적 우선 (빠른 처치)
    /// - 마무리 스킬 적극 활용
    /// - 공격 버프 후 공격
    /// - Heroic Act 활용 (Momentum 175+)
    ///
    /// 우선순위:
    /// 1. 긴급 자기 힐 (HP < 30%)
    /// 2. Heroic Act (Momentum 175+)
    /// 3. ★ 마무리 스킬 (적 HP 낮을 때)
    /// 4. 공격 버프 (PreAttackBuff)
    /// 5. 공격 (약한 적 우선)
    /// 6. PostFirstAction (Run and Gun)
    /// 7. 추가 공격
    /// 8. 턴 종료 스킬
    /// 9. 이동
    /// </summary>
    public class DPSStrategy : TimingAwareStrategy
    {
        public override string StrategyName => "DPS";

        public override ActionDecision DecideAction(ActionContext ctx)
        {
            string unitId = ctx.Unit.UniqueId;

            Main.Log($"[DPS] {ctx.Unit.CharacterName}: HP={ctx.HPPercent:F0}%, " +
                    $"{GameAPI.GetVeilStatusString()}, {GameAPI.GetMomentumStatusString()}, " +
                    $"BestTarget={ctx.BestTarget?.CharacterName ?? "none"}, " +
                    $"FirstAction={ctx.HasPerformedFirstAction}");

            // Phase 1: 긴급 자기 힐
            var healResult = TryEmergencySelfHeal(ctx);
            if (healResult != null) return healResult;

            // ★ Phase 1.5: 재장전 (v2.2.30 - 탄약 없으면 필수)
            var reloadResult = TryReload(ctx);
            if (reloadResult != null) return reloadResult;

            // ★ Phase 1.6: 원거리 캐릭터 후퇴 (v2.2.34)
            var retreatResult = TryRetreatFromEnemy(ctx);
            if (retreatResult != null) return retreatResult;

            // Phase 2: Heroic Act (Momentum 175+)
            if (GameAPI.IsHeroicActAvailable())
            {
                var heroicResult = TryUseHeroicAct(ctx);
                if (heroicResult != null) return heroicResult;
            }

            // Phase 3: ★ 마무리 스킬 우선 (적 HP 낮을 때)
            var finisherTarget = FindLowHPEnemy(ctx);
            if (finisherTarget != null)
            {
                var finisherResult = TryFinisher(ctx, finisherTarget);
                if (finisherResult != null) return finisherResult;
            }

            // Phase 4: 공격 버프 (첫 행동 전)
            // ★ v2.2.58: TurnPlanner 통합 - 전체 상황 고려한 버프 결정
            if (!ctx.HasPerformedFirstAction)
            {
                // TurnPlan이 있으면 플래너 결정 따름, 없으면 기존 로직 (폴백)
                bool shouldTryBuff = ctx.TurnPlan?.ShouldBuffFirst ?? true;

                if (shouldTryBuff)
                {
                    var buffResult = TryAttackBuffs(ctx);
                    if (buffResult != null) return buffResult;
                }
                else
                {
                    Main.LogDebug($"[DPS] ★ TurnPlanner: Skip buff - {ctx.TurnPlan?.Reason ?? "no plan"}");
                }
            }

            // Phase 4: 공격 - ★ v2.2.9: 스코어링 기반 최적 타겟 우선
            var target = ctx.BestTarget ?? ctx.WeakestEnemy ?? ctx.NearestEnemy;
            var attackResult = TryAttack(ctx, target);
            if (attackResult != null) return attackResult;

            // ★ Phase 4.5: 갭 클로저 스킬 (적이 멀리 있을 때 Death from Above 등)
            if (!ctx.IsInMeleeRange && target != null)
            {
                var gapCloserResult = TryGapCloser(ctx, target);
                if (gapCloserResult != null) return gapCloserResult;
            }

            // Phase 5: ★ PostFirstAction (Run and Gun)
            var postActionResult = TryPostFirstAction(ctx);
            if (postActionResult != null) return postActionResult;

            // Phase 6: 추가 공격
            if (ctx.HasPerformedFirstAction)
            {
                var secondAttack = TryAttackAnyEnemy(ctx);
                if (secondAttack != null) return secondAttack;
            }

            // Phase 7: 턴 종료 스킬
            var turnEndResult = TryTurnEndingAbility(ctx);
            if (turnEndResult != null) return turnEndResult;

            // ★ Phase 8: Force Basic Attack 폴백 (이동 전에 시도!)
            var basicAttackResult = TryForceBasicAttack(ctx);
            if (basicAttackResult != null) return basicAttackResult;

            // Phase 9: 이동 (공격 범위 확보) - 공격 불가능할 때만
            var moveResult = TryMoveToEnemy(ctx);
            if (moveResult != null) return moveResult;

            return ActionDecision.EndTurn("No targets available");
        }

        /// <summary>
        /// Heroic Act 사용 시도 (Momentum 175+)
        /// ★ v2.2.28: AbilityUsageTracker로 중앙화된 추적
        /// </summary>
        private ActionDecision TryUseHeroicAct(ActionContext ctx)
        {
            string unitId = ctx.Unit.UniqueId;

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsHeroicActAbility(ability)) continue;

                // 1차: 실제 버프 상태 확인 (게임 API)
                if (GameAPI.HasActiveBuff(ctx.Unit, ability))
                {
                    Main.LogDebug($"[DPS] Skip Heroic Act {ability.Name}: already active");
                    continue;
                }

                // 2차: 최근 사용 여부 확인 (프레임 기반 자동 만료)
                if (AbilityUsageTracker.WasUsedRecently(unitId, ability))
                {
                    Main.LogDebug($"[DPS] Skip Heroic Act {ability.Name}: used recently");
                    continue;
                }

                string reason;
                var target = new TargetWrapper(ctx.Unit);

                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    AbilityUsageTracker.MarkUsed(unitId, ability);
                    Main.Log($"[DPS] HEROIC ACT: {ability.Name} - Momentum={GameAPI.GetCurrentMomentum()}");
                    return ActionDecision.UseAbility(ability, target, "Heroic Act - high momentum");
                }
            }

            return null;
        }

        /// <summary>
        /// 공격 버프 우선 사용 (PreAttackBuff)
        /// ★ v2.2.12: Run and Gun 차단 + AP 예약 체크
        /// ★ v2.2.28: AbilityUsageTracker로 중앙화된 추적
        /// </summary>
        private ActionDecision TryAttackBuffs(ActionContext ctx)
        {
            string unitId = ctx.Unit.UniqueId;
            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in ctx.AvailableAbilities)
            {
                var timing = AbilityDatabase.GetTiming(ability);
                if (timing != AbilityTiming.PreAttackBuff) continue;

                // ★ v2.2.12: Run and Gun은 첫 공격 전에 절대 사용하지 않음!
                if (AbilityDatabase.IsRunAndGun(ability))
                {
                    Main.LogDebug($"[DPS] Skip Run and Gun in TryAttackBuffs (first action not performed)");
                    continue;
                }

                // PostFirstAction 타이밍 스킬은 첫 행동 전에 사용하지 않음
                if (AbilityDatabase.IsPostFirstAction(ability))
                {
                    Main.LogDebug($"[DPS] Skip {ability.Name}: PostFirstAction timing");
                    continue;
                }

                // 자기 타겟 버프만
                if (!GameAPI.IsSelfTargetAbility(ability)) continue;

                // 1차: 실제 버프 상태 확인 (게임 API)
                if (GameAPI.HasActiveBuff(ctx.Unit, ability)) continue;

                // 2차: 최근 사용 여부 확인 (프레임 기반 자동 만료)
                if (AbilityUsageTracker.WasUsedRecently(unitId, ability)) continue;

                if (!IsSafePsychicAbility(ability)) continue;

                // ★ v2.2.12: AP 예약 체크 - 무기 공격용 AP 보존
                if (!GameAPI.CanAffordAbilityWithReserve(ctx, ability, ctx.ReservedAPForAttack))
                {
                    Main.LogDebug($"[DPS] Skip {ability.Name}: not enough AP (current={ctx.CurrentAP:F1}, cost={GameAPI.GetAbilityAPCost(ability):F1}, reserved={ctx.ReservedAPForAttack:F1})");
                    continue;
                }

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    AbilityUsageTracker.MarkUsed(unitId, ability);
                    Main.Log($"[DPS] Attack buff: {ability.Name} (AP: {ctx.CurrentAP:F1} -> {ctx.CurrentAP - GameAPI.GetAbilityAPCost(ability):F1})");
                    return ActionDecision.UseAbility(ability, target, "Attack buff before strike");
                }
            }

            return null;
        }

        /// <summary>
        /// HP 낮은 적 찾기
        /// </summary>
        private BaseUnitEntity FindLowHPEnemy(ActionContext ctx)
        {
            return ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead && GameAPI.GetHPPercent(e) <= 30f)
                .OrderBy(e => GameAPI.GetHPPercent(e))
                .FirstOrDefault();
        }

        /// <summary>
        /// ★ v2.2.3: 갭 클로저 스킬 시도 (Death from Above 등)
        /// 적이 멀리 있을 때 점프 공격/돌진 등 사용
        /// ★ v2.2.7: GUID 기반으로 변경
        /// </summary>
        private ActionDecision TryGapCloser(ActionContext ctx, BaseUnitEntity target)
        {
            if (target == null) return null;

            foreach (var ability in ctx.AvailableAbilities)
            {
                // GUID 기반 갭 클로저 확인
                if (!AbilityDatabase.IsGapCloser(ability)) continue;

                // 자기 타겟이 아닌 공격성 스킬
                if (GameAPI.IsSelfTargetAbility(ability)) continue;

                // HP 소모 안전성
                if (!IsSafeHPCostAbility(ctx, ability)) continue;
                if (!IsSafePsychicAbility(ability)) continue;

                var targetWrapper = new TargetWrapper(target);
                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[DPS] ★ GAP CLOSER: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper,
                        $"Gap closer on {target.CharacterName}");
                }
                else
                {
                    // 사용 불가한 이유 로깅 (디버그)
                    Main.LogDebug($"[DPS] Gap closer {ability.Name} blocked: {reason}");
                }
            }

            return null;
        }

        /// <summary>
        /// 아무 적에게라도 공격
        /// </summary>
        private ActionDecision TryAttackAnyEnemy(ActionContext ctx)
        {
            var enemies = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetHPPercent(e)) // 약한 적 우선
                .ToList();

            var attacks = GetOffensiveAbilities(ctx.AvailableAbilities);

            foreach (var ability in attacks)
            {
                if (!IsSafeHPCostAbility(ctx, ability)) continue;
                if (!IsSafePsychicAbility(ability)) continue;

                foreach (var enemy in enemies)
                {
                    if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, enemy, ctx.Allies))
                        continue;

                    var targetWrapper = new TargetWrapper(enemy);
                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        Main.Log($"[DPS] Additional attack: {ability.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Additional attack on {enemy.CharacterName}");
                    }
                }
            }

            return null;
        }
    }
}
