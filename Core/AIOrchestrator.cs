using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.AI;
using Kingmaker.AI.BehaviourTrees;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2.Settings;
using CompanionAI_v2.Strategies;

namespace CompanionAI_v2.Core
{
    /// <summary>
    /// v2.1.0: AI Orchestrator - 전략 패턴과 게임 패치 연결
    /// 게임의 DecisionContext를 ActionContext로 변환하고 전략 실행
    /// </summary>
    public static class AIOrchestrator
    {
        #region Main Entry Point

        /// <summary>
        /// 메인 AI 결정 함수 - 패치에서 호출
        /// </summary>
        /// <param name="context">게임의 DecisionContext</param>
        /// <param name="unit">유닛</param>
        /// <param name="settings">유저 설정</param>
        /// <returns>선택된 능력과 타겟, 없으면 null</returns>
        public static (AbilityData ability, TargetWrapper target) DecideAction(
            DecisionContext context,
            BaseUnitEntity unit,
            CharacterSettings settings)
        {
            try
            {
                // 1. ActionContext 빌드
                var actionContext = BuildActionContext(context, unit, settings);
                if (actionContext == null)
                {
                    Main.Log($"[Orchestrator] Failed to build context for {unit.CharacterName}");
                    return (null, null);
                }

                // 2. 역할에 맞는 전략 가져오기
                var role = StrategyFactory.GetRoleFromSettings(settings);
                var strategy = StrategyFactory.GetStrategy(role);

                Main.Log($"[Orchestrator] {unit.CharacterName}: Role={role}, Strategy={strategy.StrategyName}");

                // 3. 전략 실행
                var decision = strategy.DecideAction(actionContext);

                if (decision == null)
                {
                    Main.Log($"[Orchestrator] {unit.CharacterName}: No decision from strategy");
                    return (null, null);
                }

                Main.Log($"[Orchestrator] {unit.CharacterName}: Decision={decision.Type}, Reason={decision.Reason}");

                // 4. 결정 타입에 따른 처리
                switch (decision.Type)
                {
                    case ActionType.UseAbility:
                        return (decision.Ability, decision.Target);

                    case ActionType.Move:
                    case ActionType.EndTurn:
                    case ActionType.Skip:
                    default:
                        // v2.1.1: 게임 AI에 위임하기 전에 HP 소모 스킬 안전 체크
                        // HP가 낮을 때 게임 AI가 Blood Oath 등을 사용하지 않도록 방지
                        var safeResult = TryPreventUnsafeAbilityDelegation(actionContext);
                        if (safeResult.ability != null)
                        {
                            Main.Log($"[Orchestrator] SAFE FALLBACK: {unit.CharacterName} uses {safeResult.ability.Name} instead of delegating to game AI");
                            return safeResult;
                        }
                        return (null, null);
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] DecideAction error: {ex}");
                return (null, null);
            }
        }

        #endregion

        #region HP Cost Ability Safety Check

        // HP 소모 스킬 안전 임계값
        private const float HP_COST_ABILITY_THRESHOLD = 40f;

        /// <summary>
        /// 게임 AI에 위임하기 전에 HP 소모 스킬 안전 체크
        /// HP가 낮고 HP 소모 스킬이 있으면 안전한 대안 능력 선택
        /// </summary>
        private static (AbilityData ability, TargetWrapper target) TryPreventUnsafeAbilityDelegation(ActionContext ctx)
        {
            // HP가 충분히 높으면 게임 AI에 위임해도 안전
            if (ctx.HPPercent > HP_COST_ABILITY_THRESHOLD)
            {
                return (null, null);
            }

            // HP가 낮을 때, HP 소모 스킬이 있는지 확인
            bool hasHPCostAbility = false;
            AbilityData safeAbility = null;

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (IsHPCostAbility(ability))
                {
                    hasHPCostAbility = true;
                    Main.LogDebug($"[Orchestrator] HP cost ability detected: {ability.Name}");
                }
                else if (safeAbility == null && GameAPI.IsOffensiveAbility(ability))
                {
                    // 첫 번째 안전한 공격 능력 저장
                    safeAbility = ability;
                }
            }

            // HP 소모 스킬이 없으면 게임 AI에 위임해도 안전
            if (!hasHPCostAbility)
            {
                return (null, null);
            }

            // HP 소모 스킬이 있고 안전한 대안이 있으면 사용
            if (safeAbility != null && ctx.NearestEnemy != null)
            {
                var targetWrapper = new TargetWrapper(ctx.NearestEnemy);
                string reason;
                if (GameAPI.CanUseAbilityOn(safeAbility, targetWrapper, out reason))
                {
                    Main.Log($"[Orchestrator] HP low ({ctx.HPPercent:F0}%), blocking game AI from using HP cost ability, using safe ability: {safeAbility.Name}");
                    return (safeAbility, targetWrapper);
                }
            }

            // 안전한 대안이 없으면 그냥 턴 종료 (게임 AI가 Blood Oath 쓰는 것보다 나음)
            // null 반환하지만 로그 남기기
            if (hasHPCostAbility)
            {
                Main.Log($"[Orchestrator] WARNING: HP low ({ctx.HPPercent:F0}%), no safe ability found, game AI may use HP cost ability");
            }

            return (null, null);
        }

        /// <summary>
        /// HP를 소모하는 스킬인지 확인 (피의 맹세 등)
        /// </summary>
        private static bool IsHPCostAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // 피의 맹세 (Blood Oath) 및 유사 HP 소모 스킬
            return name.Contains("oath") || name.Contains("맹세") ||
                   name.Contains("blood") || name.Contains("피의") ||
                   name.Contains("sacrifice") || name.Contains("희생") ||
                   bpName.Contains("oath") || bpName.Contains("blood") ||
                   bpName.Contains("sacrifice") || bpName.Contains("wound");
        }

        #endregion

        #region Context Building

        /// <summary>
        /// 게임의 DecisionContext를 ActionContext로 변환
        /// </summary>
        private static ActionContext BuildActionContext(
            DecisionContext gameContext,
            BaseUnitEntity unit,
            CharacterSettings settings)
        {
            try
            {
                var ctx = new ActionContext
                {
                    Unit = unit,
                    Settings = settings,
                    HPPercent = GameAPI.GetHPPercent(unit),
                    CanMove = GameAPI.CanMove(unit),
                    CanAct = GameAPI.CanAct(unit)
                };

                // 적/아군 수집 (팩션 기반 - 더 정확함)
                CollectUnits(unit, ctx);

                // 사용 가능한 능력 수집
                ctx.AvailableAbilities = GetAvailableAbilities(unit);

                // 타겟 분석
                AnalyzeTargets(unit, ctx);

                // 상황 분석
                AnalyzeSituation(unit, ctx);

                return ctx;
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] BuildActionContext error: {ex}");
                return null;
            }
        }

        /// <summary>
        /// 팩션 기반으로 적/아군 수집
        /// ★ v2.1.1: IsPlayerEnemy 사용하여 중립 NPC 공격 방지
        /// </summary>
        private static void CollectUnits(BaseUnitEntity unit, ActionContext ctx)
        {
            try
            {
                var allUnits = Game.Instance?.State?.AllBaseAwakeUnits;
                if (allUnits == null) return;

                foreach (var other in allUnits)
                {
                    if (other == null || other == unit) continue;
                    if (other.LifeState.IsDead) continue;
                    if (!other.IsInCombat) continue;

                    // ★ 핵심 수정: IsPlayerEnemy로 실제 적대 관계 확인
                    // IsPlayerFaction만 체크하면 중립 NPC도 적으로 분류됨
                    if (other.IsPlayerEnemy)
                    {
                        // 플레이어의 적 -> 적 목록에 추가
                        ctx.Enemies.Add(other);
                        Main.LogDebug($"[Orchestrator] Enemy detected: {other.CharacterName} (IsPlayerEnemy=true)");
                    }
                    else if (other.IsPlayerFaction)
                    {
                        // 플레이어 진영 -> 아군 목록에 추가
                        ctx.Allies.Add(other);
                    }
                    // 그 외 (중립 NPC)는 적도 아군도 아님 - 무시
                    else
                    {
                        Main.LogDebug($"[Orchestrator] Neutral unit ignored: {other.CharacterName} (IsPlayerEnemy=false, IsPlayerFaction=false)");
                    }
                }

                Main.LogDebug($"[Orchestrator] {unit.CharacterName}: Found {ctx.Enemies.Count} enemies, {ctx.Allies.Count} allies");
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] CollectUnits error: {ex}");
            }
        }

        /// <summary>
        /// 사용 가능한 능력 목록 가져오기
        /// </summary>
        private static List<AbilityData> GetAvailableAbilities(BaseUnitEntity unit)
        {
            var abilities = new List<AbilityData>();

            try
            {
                var allAbilities = unit.Abilities?.RawFacts;
                if (allAbilities == null)
                {
                    Main.LogDebug($"[Orchestrator] {unit.CharacterName}: No abilities found (RawFacts is null)");
                    return abilities;
                }

                int totalCount = 0;
                int availableCount = 0;

                foreach (var ability in allAbilities)
                {
                    var abilityData = ability?.Data;
                    if (abilityData == null) continue;

                    totalCount++;

                    // 게임 API로 사용 가능 여부 확인
                    List<string> reasons;
                    if (GameAPI.IsAbilityAvailable(abilityData, out reasons))
                    {
                        abilities.Add(abilityData);
                        availableCount++;
                    }
                    else
                    {
                        Main.LogDebug($"[Orchestrator] {unit.CharacterName}: {abilityData.Name} unavailable - {string.Join(", ", reasons)}");
                    }
                }

                Main.LogDebug($"[Orchestrator] {unit.CharacterName}: {availableCount}/{totalCount} abilities available");
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] GetAvailableAbilities error: {ex}");
            }

            return abilities;
        }

        /// <summary>
        /// 주요 타겟 분석
        /// </summary>
        private static void AnalyzeTargets(BaseUnitEntity unit, ActionContext ctx)
        {
            // 가장 가까운 적
            ctx.NearestEnemy = GameAPI.FindNearestEnemy(unit, ctx.Enemies);
            ctx.NearestEnemyDistance = ctx.NearestEnemy != null
                ? GameAPI.GetDistance(unit, ctx.NearestEnemy)
                : float.MaxValue;

            // 가장 약한 적
            ctx.WeakestEnemy = GameAPI.FindWeakestEnemy(ctx.Enemies);

            // 가장 상처입은 아군
            ctx.MostWoundedAlly = GameAPI.FindMostWoundedAlly(unit, ctx.Allies);
        }

        /// <summary>
        /// 상황 분석
        /// </summary>
        private static void AnalyzeSituation(BaseUnitEntity unit, ActionContext ctx)
        {
            // 근접 교전 여부
            ctx.EnemiesInMeleeRange = 0;
            foreach (var enemy in ctx.Enemies)
            {
                if (GameAPI.IsInMeleeRange(unit, enemy))
                {
                    ctx.EnemiesInMeleeRange++;
                }
            }
            ctx.IsInMeleeRange = ctx.EnemiesInMeleeRange > 0;

            // 무기 타입 확인
            ctx.HasMeleeWeapon = HasWeaponOfType(unit, true);
            ctx.HasRangedWeapon = HasWeaponOfType(unit, false);
        }

        /// <summary>
        /// 유닛이 특정 타입의 무기를 가지고 있는지 확인
        /// </summary>
        private static bool HasWeaponOfType(BaseUnitEntity unit, bool melee)
        {
            try
            {
                var weapon = unit.GetFirstWeapon();
                if (weapon == null) return false;

                return melee ? weapon.Blueprint.IsMelee : !weapon.Blueprint.IsMelee;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
