using System.Linq;
using Kingmaker.Utility;
using CompanionAI_v2_2.Core;

namespace CompanionAI_v2_2.Strategies
{
    /// <summary>
    /// v2.2.0: Balanced 전략 - 타이밍 인식 만능형
    ///
    /// 우선순위:
    /// 1. 긴급 자기 힐 (HP < 30%)
    /// 2. 선제적 버프 (첫 행동 전)
    /// 3. 디버프 적용
    /// 4. 공격 (가장 효율적인 대상)
    /// 5. ★ PostFirstAction (Run and Gun 등) - 첫 행동 후
    /// 6. 추가 공격 (PostFirstAction 후)
    /// 7. 턴 종료 스킬
    /// 8. 이동
    /// </summary>
    public class BalancedStrategy : TimingAwareStrategy
    {
        public override string StrategyName => "Balanced";

        public override ActionDecision DecideAction(ActionContext ctx)
        {
            Main.Log($"[Balanced] {ctx.Unit.CharacterName}: HP={ctx.HPPercent:F0}%, " +
                    $"{GameAPI.GetVeilStatusString()}, {GameAPI.GetMomentumStatusString()}, " +
                    $"Enemies={ctx.Enemies.Count}, FirstAction={ctx.HasPerformedFirstAction}");

            // Phase 1: 긴급 자기 힐
            var healResult = TryEmergencySelfHeal(ctx);
            if (healResult != null) return healResult;

            // ★ Phase 1.5: 재장전 (v2.2.30 - 탄약 없으면 필수)
            var reloadResult = TryReload(ctx);
            if (reloadResult != null) return reloadResult;

            // Phase 2: 선제적 버프 (첫 행동 전에만)
            var buffResult = TryProactiveBuffs(ctx);
            if (buffResult != null) return buffResult;

            // 타겟 선택 - ★ v2.2.9: 무기 타입별 스코어링 기반
            var target = ctx.IsInMeleeRange && ctx.HasMeleeWeapon
                ? ctx.BestMeleeTarget ?? ctx.NearestEnemy
                : ctx.BestRangedTarget ?? ctx.BestTarget ?? ctx.NearestEnemy;

            // Phase 3: 디버프 적용
            var debuffResult = TryDebuffs(ctx, target);
            if (debuffResult != null) return debuffResult;

            // Phase 4: 공격
            var attackResult = TryAttack(ctx, target);
            if (attackResult != null) return attackResult;

            // Phase 5: ★ PostFirstAction (Run and Gun 등)
            var postActionResult = TryPostFirstAction(ctx);
            if (postActionResult != null) return postActionResult;

            // Phase 5.5: PostFirstAction 후 추가 공격
            if (ctx.HasPerformedFirstAction)
            {
                var secondAttack = TryAttackAnyEnemy(ctx);
                if (secondAttack != null) return secondAttack;
            }

            // Phase 6: 턴 종료 스킬
            var turnEndResult = TryTurnEndingAbility(ctx);
            if (turnEndResult != null) return turnEndResult;

            // ★ Phase 7: Force Basic Attack 폴백 (이동 전에 시도!)
            var basicAttackResult = TryForceBasicAttack(ctx);
            if (basicAttackResult != null) return basicAttackResult;

            // Phase 8: 이동 - 공격 불가능할 때만
            var moveResult = TryMoveToEnemy(ctx);
            if (moveResult != null) return moveResult;

            return ActionDecision.EndTurn("No valid action");
        }

        /// <summary>
        /// 아무 적에게라도 공격 시도
        /// </summary>
        private ActionDecision TryAttackAnyEnemy(ActionContext ctx)
        {
            var enemies = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetDistance(ctx.Unit, e))
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
                        Main.Log($"[Balanced] Secondary attack: {ability.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Secondary attack on {enemy.CharacterName}");
                    }
                }
            }

            return null;
        }
    }
}
