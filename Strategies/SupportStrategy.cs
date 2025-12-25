using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.Utility;
using UnityEngine;
using CompanionAI_v2_2.Core;
using CompanionAI_v2_2.Settings;

namespace CompanionAI_v2_2.Strategies
{
    /// <summary>
    /// v2.2.0: Support 전략 - 타이밍 인식 지원형
    /// ★ v2.2.28: AbilityUsageTracker로 중앙화된 추적 시스템 사용
    ///
    /// 특징:
    /// - 아군 버프/힐 우선
    /// - 적 디버프 활용
    /// - 안전 거리 유지
    /// - 수류탄/폭발물 안전 처리
    /// - 원뿔형 AoE 안전 체크
    ///
    /// 우선순위:
    /// 1. 긴급 자기 힐 (HP < 30%)
    /// 2. 모멘텀 생성 (Desperate 상태)
    /// 3. 아군 힐 (HP < 50%)
    /// 4. 선제적 버프
    /// 5. 아군 버프
    /// 6. 적 디버프
    /// 7. 안전한 원거리 공격
    /// 8. PostFirstAction
    /// 9. 턴 종료 스킬
    /// 10. 후퇴
    /// </summary>
    public class SupportStrategy : TimingAwareStrategy
    {
        public override string StrategyName => "Support";

        public override ActionDecision DecideAction(ActionContext ctx)
        {
            string unitId = ctx.Unit.UniqueId;

            Main.Log($"[Support] {ctx.Unit.CharacterName}: HP={ctx.HPPercent:F0}%, " +
                    $"{GameAPI.GetVeilStatusString()}, {GameAPI.GetMomentumStatusString()}, " +
                    $"Allies={ctx.Allies.Count}, Enemies={ctx.Enemies.Count}");

            // Phase 1: 긴급 자기 힐
            var healResult = TryEmergencySelfHeal(ctx);
            if (healResult != null) return healResult;

            // ★ Phase 1.5: 재장전 (v2.2.30 - 탄약 없으면 필수)
            var reloadResult = TryReload(ctx);
            if (reloadResult != null) return reloadResult;

            // Phase 2: 모멘텀이 낮으면 모멘텀 생성 스킬 우선
            if (GameAPI.IsDesperateMeasures())
            {
                var momentumResult = TryUseMomentumGeneratingAbility(ctx);
                if (momentumResult != null) return momentumResult;
            }

            // Phase 3: 아군 힐 (HP < 50%)
            var allyHealResult = TryHealAlly(ctx, 50f);
            if (allyHealResult != null) return allyHealResult;

            // Phase 4: 선제적 자기 버프
            var selfBuffResult = TryProactiveBuffs(ctx);
            if (selfBuffResult != null) return selfBuffResult;

            // Phase 5: 아군 버프
            var allyBuffResult = TryBuffAlly(ctx);
            if (allyBuffResult != null) return allyBuffResult;

            // Phase 6: 적 디버프
            var debuffResult = TryDebuffs(ctx, ctx.NearestEnemy);
            if (debuffResult != null) return debuffResult;

            // Phase 7: 안전한 원거리 공격
            var attackResult = TrySafeRangedAttack(ctx);
            if (attackResult != null) return attackResult;

            // Phase 8: PostFirstAction
            var postActionResult = TryPostFirstAction(ctx);
            if (postActionResult != null) return postActionResult;

            // Phase 9: 턴 종료 스킬
            var turnEndResult = TryTurnEndingAbility(ctx);
            if (turnEndResult != null) return turnEndResult;

            // ★ Phase 10: Force Basic Attack 폴백 (이동/후퇴 전에 시도!)
            var basicAttackResult = TryForceBasicAttack(ctx);
            if (basicAttackResult != null) return basicAttackResult;

            // Phase 11: 안전 거리 유지 - 공격 불가능할 때만
            var retreatResult = TryMaintainDistance(ctx);
            if (retreatResult != null) return retreatResult;

            return ActionDecision.EndTurn("Supporting from distance");
        }

        /// <summary>
        /// 모멘텀 생성 스킬 사용 (War Hymn, Assign Objective 등)
        /// ★ v2.2.28: AbilityUsageTracker로 중앙화된 추적
        /// </summary>
        private ActionDecision TryUseMomentumGeneratingAbility(ActionContext ctx)
        {
            string unitId = ctx.Unit.UniqueId;

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsMomentumGeneratingAbility(ability)) continue;
                if (!IsSafePsychicAbility(ability)) continue;

                TargetWrapper target = null;
                string targetId = null;

                if (GameAPI.IsSelfTargetAbility(ability))
                {
                    target = new TargetWrapper(ctx.Unit);
                    targetId = ctx.Unit.UniqueId;
                }
                else if (GameAPI.IsOffensiveAbility(ability) && ctx.NearestEnemy != null)
                {
                    target = new TargetWrapper(ctx.NearestEnemy);
                    targetId = ctx.NearestEnemy.UniqueId;
                }
                else if (GameAPI.IsSupportAbility(ability))
                {
                    var bestAlly = ctx.Allies
                        .Where(a => a != null && !a.LifeState.IsDead)
                        .OrderByDescending(a => GameAPI.GetHPPercent(a))
                        .FirstOrDefault();
                    if (bestAlly != null)
                    {
                        target = new TargetWrapper(bestAlly);
                        targetId = bestAlly.UniqueId;
                    }
                }

                if (target == null || targetId == null) continue;

                // 특정 타겟에 대해 최근 사용 여부 확인
                if (AbilityUsageTracker.WasUsedOnTargetRecently(unitId, AbilityUsageTracker.GetAbilityId(ability), targetId))
                    continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    AbilityUsageTracker.MarkUsedOnTarget(unitId, ability, targetId);
                    Main.Log($"[Support] MOMENTUM BOOST: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Momentum generating ability");
                }
            }
            return null;
        }

        /// <summary>
        /// 아군 힐
        /// </summary>
        private ActionDecision TryHealAlly(ActionContext ctx, float hpThreshold)
        {
            var targets = new List<BaseUnitEntity> { ctx.Unit };
            targets.AddRange(ctx.Allies.Where(a => a != null && !a.LifeState.IsDead));

            var woundedTargets = targets
                .Where(t => GameAPI.GetHPPercent(t) < hpThreshold)
                .OrderBy(t => GameAPI.GetHPPercent(t))
                .ToList();

            if (woundedTargets.Count == 0) return null;

            foreach (var target in woundedTargets)
            {
                var targetWrapper = new TargetWrapper(target);

                foreach (var ability in ctx.AvailableAbilities)
                {
                    if (!IsHealAbility(ability)) continue;
                    if (!IsSafePsychicAbility(ability)) continue;

                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        Main.Log($"[Support] Heal: {ability.Name} -> {target.CharacterName}");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Heal {target.CharacterName}");
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 아군 버프 - ★ v2.2.1: 우선순위 Tank > DPS > 기타
        /// ★ v2.2.28: AbilityUsageTracker로 중앙화된 추적
        /// </summary>
        private ActionDecision TryBuffAlly(ActionContext ctx)
        {
            string unitId = ctx.Unit.UniqueId;

            // ★ 버프 대상 우선순위: Tank > DPS > 본인 > 기타
            var prioritizedTargets = new List<BaseUnitEntity>();

            // 1. Tank 역할 먼저
            foreach (var ally in ctx.Allies.Where(a => a != null && !a.LifeState.IsDead))
            {
                var settings = Main.Settings?.GetOrCreateSettings(ally.UniqueId, ally.CharacterName);
                if (settings?.Role == AIRole.Tank)
                    prioritizedTargets.Add(ally);
            }

            // 2. DPS 역할
            foreach (var ally in ctx.Allies.Where(a => a != null && !a.LifeState.IsDead))
            {
                var settings = Main.Settings?.GetOrCreateSettings(ally.UniqueId, ally.CharacterName);
                if (settings?.Role == AIRole.DPS && !prioritizedTargets.Contains(ally))
                    prioritizedTargets.Add(ally);
            }

            // 3. 본인
            prioritizedTargets.Add(ctx.Unit);

            // 4. 나머지 아군
            foreach (var ally in ctx.Allies.Where(a => a != null && !a.LifeState.IsDead))
            {
                if (!prioritizedTargets.Contains(ally))
                    prioritizedTargets.Add(ally);
            }

            foreach (var ability in ctx.AvailableAbilities)
            {
                // 수류탄/폭발물 제외
                if (CombatHelpers.IsGrenadeOrExplosive(ability)) continue;
                // 무기 공격 제외
                if (IsWeaponAttack(ability)) continue;
                // 힐 제외
                if (IsHealAbility(ability)) continue;
                // 지원 능력만
                if (!GameAPI.IsSupportAbility(ability) && !GameAPI.IsSelfTargetAbility(ability)) continue;

                if (!IsSafePsychicAbility(ability)) continue;

                foreach (var target in prioritizedTargets)
                {
                    // 1차: 실제 버프 상태 확인 (게임 API)
                    if (GameAPI.HasActiveBuff(target, ability)) continue;

                    // 2차: 특정 타겟에 대해 최근 사용 여부 확인
                    if (AbilityUsageTracker.WasUsedOnTargetRecently(unitId, AbilityUsageTracker.GetAbilityId(ability), target.UniqueId))
                        continue;

                    var targetWrapper = new TargetWrapper(target);
                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        AbilityUsageTracker.MarkUsedOnTarget(unitId, ability, target.UniqueId);
                        Main.Log($"[Support] Buff: {ability.Name} -> {target.CharacterName}");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Buff {target.CharacterName}");
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 안전한 원거리 공격 - AoE 및 원뿔 체크 강화
        /// </summary>
        private ActionDecision TrySafeRangedAttack(ActionContext ctx)
        {
            if (ctx.IsInMeleeRange && ctx.CanMove) return null;

            var enemies = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetDistance(ctx.Unit, e))
                .ToList();

            var safeAbilities = ctx.AvailableAbilities
                .Where(a => !GameAPI.IsMeleeAbility(a))
                .Where(a => !CombatHelpers.IsGrenadeOrExplosive(a) || enemies.Count > 0)
                .OrderBy(a => CombatHelpers.IsAoEAbility(a) ? 1 : 0)
                .ToList();

            foreach (var ability in safeAbilities)
            {
                if (!IsSafeHPCostAbility(ctx, ability)) continue;
                if (!IsSafePsychicAbility(ability)) continue;

                bool isAoE = CombatHelpers.IsAoEAbility(ability);
                bool isCone = IsConeAbility(ability);
                bool isLidlessStare = IsLidlessStareAbility(ability);  // ★ Navigator 응시

                foreach (var enemy in enemies)
                {
                    // ★ v2.2.1: 원뿔형 AoE 체크
                    if (isCone)
                    {
                        // Navigator의 Lidless Stare는 매우 넓은 원뿔 - 0명만 허용!
                        int maxAlliesAllowed = isLidlessStare ? 0 : 1;
                        int alliesInCone = CountAlliesInCone(ctx, enemy.Position, isLidlessStare ? 60f : 40f);

                        if (alliesInCone > maxAlliesAllowed)
                        {
                            Main.LogDebug($"[Support] CONE BLOCKED: {alliesInCone} allies in cone (max={maxAlliesAllowed})");
                            continue;
                        }
                    }
                    // 일반 AoE 체크
                    else if (isAoE)
                    {
                        if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, enemy, ctx.Allies))
                            continue;
                    }

                    var targetWrapper = new TargetWrapper(enemy);
                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        Main.Log($"[Support] Attack: {ability.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Attack {enemy.CharacterName}");
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 안전 거리 유지
        /// </summary>
        private ActionDecision TryMaintainDistance(ActionContext ctx)
        {
            if (!ctx.CanMove) return null;

            if (ctx.IsInMeleeRange || ctx.EnemiesInMeleeRange > 0)
            {
                Main.Log("[Support] Retreating to safe distance");
                return ActionDecision.Move("Retreat to safe distance");
            }
            return null;
        }

        #region Helpers

        private bool IsWeaponAttack(AbilityData ability)
        {
            if (ability == null) return false;

            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            string name = ability.Name?.ToLower() ?? "";

            // 수류탄/폭발물은 먼저 체크
            if (CombatHelpers.IsGrenadeOrExplosive(ability)) return true;

            // 원뿔 공격
            if (name.Contains("응시") || name.Contains("stare") ||
                bpName.Contains("lidless") || bpName.Contains("gaze") ||
                bpName.Contains("cone")) return true;

            // 버프 키워드는 무기가 아님
            if (bpName.Contains("leader") || bpName.Contains("support") ||
                bpName.Contains("buff") || bpName.Contains("inspire")) return false;

            // 무기 공격 키워드
            if (bpName.Contains("meleeattack") || bpName.Contains("singleshot") ||
                bpName.Contains("burstfire") || bpName.Contains("fullburst")) return true;

            if (ability.Weapon != null) return true;

            return false;
        }

        private bool IsConeAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            return name.Contains("응시") || name.Contains("stare") ||
                   name.Contains("cone") || name.Contains("breath") ||
                   bpName.Contains("lidless") || bpName.Contains("cone") ||
                   bpName.Contains("gaze");
        }

        /// <summary>
        /// ★ v2.2.1: Navigator의 Lidless Stare 감지 - 매우 넓은 원뿔이므로 특별 처리 필요
        /// </summary>
        private bool IsLidlessStareAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // Lidless Stare / 눈꺼풀 없는 응시
            return name.Contains("눈꺼풀") || name.Contains("lidless") ||
                   bpName.Contains("lidlessstare") || bpName.Contains("lidless_stare");
        }

        private int CountAlliesInCone(ActionContext ctx, Vector3 targetPosition, float coneAngle)
        {
            int count = 0;
            var casterPos = ctx.Unit.Position;
            var toTarget = targetPosition - casterPos;
            float targetDistance = toTarget.magnitude;
            var targetDirection = toTarget.normalized;
            float maxCheckDistance = targetDistance + 5f;

            foreach (var ally in ctx.Allies)
            {
                if (ally == null || ally.LifeState.IsDead) continue;

                var toAlly = ally.Position - casterPos;
                float allyDistance = toAlly.magnitude;

                if (allyDistance < 1f || allyDistance > maxCheckDistance) continue;

                var allyDirection = toAlly.normalized;
                float dotProduct = Vector3.Dot(targetDirection, allyDirection);
                float angle = Mathf.Acos(Mathf.Clamp(dotProduct, -1f, 1f)) * Mathf.Rad2Deg;

                if (angle <= coneAngle / 2f)
                {
                    count++;
                    Main.LogDebug($"[Support] Ally {ally.CharacterName} in cone at {angle:F1}°");
                }
            }
            return count;
        }

        #endregion
    }
}
