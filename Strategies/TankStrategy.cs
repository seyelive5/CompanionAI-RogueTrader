using System.Collections.Generic;
using System.Linq;
using Kingmaker.Utility;
using CompanionAI_v2_2.Core;

namespace CompanionAI_v2_2.Strategies
{
    /// <summary>
    /// v2.2.0: Tank 전략 - 타이밍 인식 방어형
    ///
    /// 특징:
    /// - 방어 자세 우선 (PreCombatBuff)
    /// - 적 어그로 유인 (2명 이상일 때)
    /// - 아군 보호
    ///
    /// 우선순위:
    /// 1. 긴급 자기 힐 (HP < 30%)
    /// 2. ★ 방어 자세 버프 우선 (Defensive Stance)
    /// 3. 선제적 버프
    /// 4. 적 도발/어그로 (근접 2명 이상)
    /// 5. 공격 (가까운 적)
    /// 6. PostFirstAction
    /// 7. 턴 종료 스킬
    /// 8. 이동 (적 사이로)
    /// </summary>
    public class TankStrategy : TimingAwareStrategy
    {
        public override string StrategyName => "Tank";

        // 이번 턴에 사용한 능력 추적 (중복 버프 방지)
        private static HashSet<string> _usedAbilityTargetPairs = new HashSet<string>();
        private static string _lastUnitId = null;

        public override ActionDecision DecideAction(ActionContext ctx)
        {
            // 유닛이 바뀌면 추적 초기화
            string unitId = ctx.Unit.UniqueId;
            if (_lastUnitId != unitId)
            {
                _usedAbilityTargetPairs.Clear();
                _lastUnitId = unitId;
            }

            Main.Log($"[Tank] {ctx.Unit.CharacterName}: HP={ctx.HPPercent:F0}%, " +
                    $"{GameAPI.GetVeilStatusString()}, {GameAPI.GetMomentumStatusString()}, " +
                    $"EnemiesInMelee={ctx.EnemiesInMeleeRange}, FirstAction={ctx.HasPerformedFirstAction}");

            // Phase 1: 긴급 자기 힐
            var healResult = TryEmergencySelfHeal(ctx);
            if (healResult != null) return healResult;

            // Phase 2: ★ 방어 자세 우선 (첫 행동 전)
            if (!ctx.HasPerformedFirstAction)
            {
                var defenseResult = TryDefensiveStance(ctx);
                if (defenseResult != null) return defenseResult;
            }

            // Phase 3: 기타 선제적 버프
            var buffResult = TryProactiveBuffs(ctx);
            if (buffResult != null) return buffResult;

            // Phase 4: 적 도발 (있으면)
            var tauntResult = TryTaunt(ctx);
            if (tauntResult != null) return tauntResult;

            // Phase 5: 공격 - 가장 가까운 적 우선 (어그로 유지)
            var attackResult = TryAttack(ctx, ctx.NearestEnemy);
            if (attackResult != null) return attackResult;

            // Phase 6: PostFirstAction
            var postActionResult = TryPostFirstAction(ctx);
            if (postActionResult != null) return postActionResult;

            // Phase 7: 턴 종료 스킬
            var turnEndResult = TryTurnEndingAbility(ctx);
            if (turnEndResult != null) return turnEndResult;

            // ★ Phase 8: Force Basic Attack 폴백 (이동 전에 시도!)
            var basicAttackResult = TryForceBasicAttack(ctx);
            if (basicAttackResult != null) return basicAttackResult;

            // Phase 9: 이동 - 적 사이로 (공격 불가능할 때만)
            var moveResult = TryMoveToFrontline(ctx);
            if (moveResult != null) return moveResult;

            return ActionDecision.EndTurn("Holding position");
        }

        /// <summary>
        /// 방어 자세 스킬 우선 사용
        /// </summary>
        private ActionDecision TryDefensiveStance(ActionContext ctx)
        {
            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsDefensiveStanceAbility(ability)) continue;
                if (GameAPI.HasActiveBuff(ctx.Unit, ability)) continue;

                // 중복 방지
                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;
                string pairKey = $"{abilityId}:{ctx.Unit.UniqueId}";
                if (_usedAbilityTargetPairs.Contains(pairKey)) continue;

                // Veil 안전성 체크
                if (!IsSafePsychicAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    _usedAbilityTargetPairs.Add(pairKey);
                    Main.Log($"[Tank] Defensive stance: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Defensive stance priority");
                }
            }

            return null;
        }

        /// <summary>
        /// 도발/어그로 스킬 (근접 적 2명 이상일 때)
        /// ★ v2.2.7: GUID 기반으로 변경
        /// </summary>
        private ActionDecision TryTaunt(ActionContext ctx)
        {
            // 도발 조건: 근처에 적이 2명 이상
            if (ctx.EnemiesInMeleeRange < 2) return null;

            foreach (var ability in ctx.AvailableAbilities)
            {
                // GUID 기반 도발 스킬 확인
                if (!AbilityGuids.IsTaunt(ability)) continue;

                // 버프 활성 체크
                if (GameAPI.HasActiveBuff(ctx.Unit, ability))
                {
                    Main.LogDebug($"[Tank] Skip taunt {ability.Name}: already active");
                    continue;
                }

                // 도발이 자기 타겟인지 적 타겟인지 확인
                TargetWrapper targetWrapper;
                string targetName;

                if (GameAPI.IsSelfTargetAbility(ability))
                {
                    targetWrapper = new TargetWrapper(ctx.Unit);
                    targetName = "self";
                }
                else
                {
                    var target = ctx.NearestEnemy;
                    if (target == null) continue;
                    targetWrapper = new TargetWrapper(target);
                    targetName = target.CharacterName;
                }

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[Tank] Taunt: {ability.Name} -> {targetName} ({ctx.EnemiesInMeleeRange} enemies nearby)");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Taunt - multiple enemies nearby");
                }
            }

            return null;
        }

        /// <summary>
        /// 전선으로 이동
        /// ★ v2.2.2: 이동 대신 사거리 밖 공격 시도 (게임 AI가 알아서 이동+공격)
        /// </summary>
        private ActionDecision TryMoveToFrontline(ActionContext ctx)
        {
            if (!ctx.CanMove) return null;
            if (ctx.NearestEnemy == null) return null;

            // 근접 범위에 적이 없으면
            if (ctx.EnemiesInMeleeRange == 0)
            {
                // ★ v2.2.2: 이동 대신 사거리 밖 공격 선택 (게임이 자동으로 이동+공격)
                var attackWithMove = TryForceAttackIgnoringRange(ctx);
                if (attackWithMove != null) return attackWithMove;

                // 폴백: 게임 AI에 이동 위임
                Main.Log("[Tank] Moving to frontline");
                return ActionDecision.Move("Move to frontline");
            }

            return null;
        }
    }
}
