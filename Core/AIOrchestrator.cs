using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.AI;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2_2.Settings;
using CompanionAI_v2_2.Strategies;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.0: AI 오케스트레이터 - 전략 조율 및 행동 추적
    ///
    /// 핵심 역할:
    /// 1. 턴 내 행동 추적 (HasPerformedFirstAction 등)
    /// 2. 전략 선택 및 호출
    /// 3. 타이밍 인식 결정
    /// </summary>
    public static class AIOrchestrator
    {
        // 현재 턴에 행동한 유닛 추적
        private static Dictionary<string, TurnState> _turnStates = new Dictionary<string, TurnState>();

        // HP 소모 스킬 안전 임계값
        private const float HP_COST_ABILITY_THRESHOLD = 40f;

        // ★ v2.2.1: 라운드 추적
        private static int _lastKnownRound = 0;
        private static int _combatStartFrame = 0;

        private class TurnState
        {
            public bool HasPerformedFirstAction { get; set; } = false;
            public int ActionsPerformed { get; set; } = 0;
            public HashSet<string> UsedAbilities { get; set; } = new HashSet<string>();
        }

        #region Main Entry Point

        /// <summary>
        /// 메인 AI 결정 함수 - 패치에서 호출
        /// </summary>
        public static (AbilityData ability, TargetWrapper target) DecideAction(
            DecisionContext gameContext,
            BaseUnitEntity unit,
            CharacterSettings settings)
        {
            try
            {
                // 턴 변경 감지
                CheckTurnChange();

                // 턴 상태 가져오기
                var turnState = GetOrCreateTurnState(unit);

                // ActionContext 빌드
                var ctx = BuildActionContext(gameContext, unit, settings, turnState);
                if (ctx == null)
                {
                    Main.Log($"[Orchestrator] Failed to build context for {unit.CharacterName}");
                    return (null, null);
                }

                // 전략 가져오기
                var strategy = StrategyFactory.GetStrategy(settings.Role);

                Main.Log($"[Orchestrator] {unit.CharacterName}: Role={settings.Role}, Strategy={strategy.StrategyName}, " +
                        $"FirstAction={turnState.HasPerformedFirstAction}, Actions={turnState.ActionsPerformed}");

                // 전략 실행
                var decision = strategy.DecideAction(ctx);

                if (decision == null)
                {
                    Main.Log($"[Orchestrator] {unit.CharacterName}: No decision from strategy");
                    return (null, null);
                }

                Main.Log($"[Orchestrator] {unit.CharacterName}: Decision={decision.Type}, Reason={decision.Reason}");

                // 결정 타입에 따른 처리
                switch (decision.Type)
                {
                    case ActionType.UseAbility:
                        if (decision.Ability != null && decision.Target != null)
                        {
                            // 턴 상태 업데이트
                            UpdateTurnState(unit, decision.Ability, turnState);
                            return (decision.Ability, decision.Target);
                        }
                        break;

                    case ActionType.Move:
                    case ActionType.EndTurn:
                    case ActionType.Skip:
                    default:
                        // HP 소모 스킬 안전 체크
                        var safeResult = TryPreventUnsafeAbilityDelegation(ctx);
                        if (safeResult.ability != null)
                        {
                            Main.Log($"[Orchestrator] SAFE FALLBACK: {unit.CharacterName} uses {safeResult.ability.Name}");
                            return safeResult;
                        }
                        break;
                }

                return (null, null);
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] DecideAction error: {ex}");
                return (null, null);
            }
        }

        #endregion

        #region Context Building

        private static ActionContext BuildActionContext(
            DecisionContext gameContext,
            BaseUnitEntity unit,
            CharacterSettings settings,
            TurnState turnState)
        {
            try
            {
                var ctx = new ActionContext
                {
                    Unit = unit,
                    Settings = settings,
                    HPPercent = GameAPI.GetHPPercent(unit),
                    CanMove = GameAPI.CanMove(unit),
                    CanAct = GameAPI.CanAct(unit),
                    HasPerformedFirstAction = turnState.HasPerformedFirstAction,
                    ActionsPerformedThisTurn = turnState.ActionsPerformed
                };

                // 적/아군 수집
                CollectUnits(unit, ctx);

                // 사용 가능한 능력 수집
                ctx.AvailableAbilities = GetAvailableAbilities(unit, turnState);

                // 타겟 분석
                ctx.NearestEnemy = GameAPI.FindNearestEnemy(unit, ctx.Enemies);
                ctx.NearestEnemyDistance = ctx.NearestEnemy != null
                    ? GameAPI.GetDistance(unit, ctx.NearestEnemy)
                    : float.MaxValue;
                ctx.WeakestEnemy = GameAPI.FindWeakestEnemy(ctx.Enemies);
                ctx.MostWoundedAlly = GameAPI.FindMostWoundedAlly(unit, ctx.Allies);

                // ★ v2.2.9: 스코어링 기반 최적 타겟
                ctx.BestTarget = GameAPI.FindBestTarget(unit, ctx.Enemies);
                ctx.BestMeleeTarget = GameAPI.FindBestTargetForWeapon(unit, ctx.Enemies, isMelee: true);
                ctx.BestRangedTarget = GameAPI.FindBestTargetForWeapon(unit, ctx.Enemies, isMelee: false);

                // 상황 분석
                ctx.EnemiesInMeleeRange = 0;
                foreach (var enemy in ctx.Enemies)
                {
                    if (GameAPI.IsInMeleeRange(unit, enemy))
                        ctx.EnemiesInMeleeRange++;
                }
                ctx.IsInMeleeRange = ctx.EnemiesInMeleeRange > 0;

                // 무기 타입 확인
                ctx.HasMeleeWeapon = HasWeaponOfType(unit, true);
                ctx.HasRangedWeapon = HasWeaponOfType(unit, false);

                return ctx;
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] BuildActionContext error: {ex}");
                return null;
            }
        }

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

                    // IsPlayerEnemy로 실제 적대 관계 확인
                    if (other.IsPlayerEnemy)
                    {
                        ctx.Enemies.Add(other);
                    }
                    else if (other.IsPlayerFaction)
                    {
                        ctx.Allies.Add(other);
                    }
                }

                Main.LogDebug($"[Orchestrator] {unit.CharacterName}: Found {ctx.Enemies.Count} enemies, {ctx.Allies.Count} allies");
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] CollectUnits error: {ex}");
            }
        }

        private static List<AbilityData> GetAvailableAbilities(BaseUnitEntity unit, TurnState turnState)
        {
            var abilities = new List<AbilityData>();

            try
            {
                var allAbilities = unit.Abilities?.RawFacts;
                if (allAbilities == null) return abilities;

                foreach (var ability in allAbilities)
                {
                    var abilityData = ability?.Data;
                    if (abilityData == null) continue;

                    // 게임 API로 사용 가능 여부 확인
                    List<string> reasons;
                    if (!GameAPI.IsAbilityAvailable(abilityData, out reasons))
                        continue;

                    // 전투당 1회 제한 스킬 체크
                    var rule = AbilityRulesDatabase.GetRule(abilityData);
                    if (rule?.SingleUsePerCombat == true)
                    {
                        string abilityId = abilityData.Blueprint?.AssetGuid?.ToString() ?? abilityData.Name;
                        if (turnState.UsedAbilities.Contains(abilityId))
                        {
                            Main.LogDebug($"[Orchestrator] Skip {abilityData.Name}: already used this combat");
                            continue;
                        }
                    }

                    abilities.Add(abilityData);
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] GetAvailableAbilities error: {ex}");
            }

            return abilities;
        }

        private static bool HasWeaponOfType(BaseUnitEntity unit, bool melee)
        {
            try
            {
                var weapon = unit.GetFirstWeapon();
                if (weapon == null) return false;
                return melee ? weapon.Blueprint.IsMelee : !weapon.Blueprint.IsMelee;
            }
            catch { return false; }
        }

        #endregion

        #region Turn State Management

        private static void UpdateTurnState(BaseUnitEntity unit, AbilityData ability, TurnState turnState)
        {
            var timing = AbilityRulesDatabase.GetTiming(ability);

            // 첫 행동 마킹 (버프가 아닌 실제 행동)
            if (!turnState.HasPerformedFirstAction)
            {
                if (timing != AbilityTiming.PreCombatBuff &&
                    timing != AbilityTiming.PreAttackBuff &&
                    timing != AbilityTiming.StackingBuff)
                {
                    turnState.HasPerformedFirstAction = true;
                    Main.Log($"[Orchestrator] {unit.CharacterName}: First action performed with {ability.Name}");
                }
            }

            turnState.ActionsPerformed++;

            // 1회 제한 스킬 기록
            var rule = AbilityRulesDatabase.GetRule(ability);
            if (rule?.SingleUsePerCombat == true)
            {
                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;
                turnState.UsedAbilities.Add(abilityId);
            }
        }

        /// <summary>
        /// ★ v2.2.1: 턴/라운드 변경 감지 및 상태 정리
        /// - 라운드가 바뀌면 모든 유닛의 HasPerformedFirstAction 초기화
        /// - 사망한 유닛의 상태 정리 (메모리 누수 방지)
        /// </summary>
        private static void CheckTurnChange()
        {
            try
            {
                // 현재 라운드 가져오기
                int currentRound = GetCurrentCombatRound();

                // 라운드 변경 감지
                if (currentRound > _lastKnownRound && _lastKnownRound > 0)
                {
                    Main.Log($"[Orchestrator] ★ ROUND CHANGE: {_lastKnownRound} -> {currentRound}");
                    ResetAllFirstActionFlags();
                }
                _lastKnownRound = currentRound;

                // 사망한 유닛 상태 정리 (매 5번째 호출마다 실행 - 성능 최적화)
                if (UnityEngine.Time.frameCount % 60 == 0)
                {
                    CleanupDeadUnitStates();
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] CheckTurnChange error: {ex}");
            }
        }

        /// <summary>
        /// 현재 전투 라운드 번호 가져오기
        /// </summary>
        private static int GetCurrentCombatRound()
        {
            try
            {
                // TurnController에서 라운드 정보 가져오기
                var turnController = Game.Instance?.TurnController;
                if (turnController != null)
                {
                    // GameTime.GetRound()를 통해 라운드 계산
                    // 또는 TurnController의 라운드 정보 직접 접근
                    var combatRound = turnController.CombatRound;
                    return combatRound;
                }

                // 폴백: 프레임 기반 추정 (6초 = 1라운드 가정)
                if (_combatStartFrame == 0)
                {
                    _combatStartFrame = UnityEngine.Time.frameCount;
                }
                float elapsedTime = (UnityEngine.Time.frameCount - _combatStartFrame) / 60f; // 60fps 가정
                return (int)(elapsedTime / 6f) + 1; // 6초당 1라운드
            }
            catch
            {
                return 1;
            }
        }

        /// <summary>
        /// 모든 유닛의 HasPerformedFirstAction 플래그 초기화
        /// </summary>
        private static void ResetAllFirstActionFlags()
        {
            int resetCount = 0;
            foreach (var kvp in _turnStates)
            {
                if (kvp.Value.HasPerformedFirstAction)
                {
                    kvp.Value.HasPerformedFirstAction = false;
                    kvp.Value.ActionsPerformed = 0;
                    resetCount++;
                }
            }
            Main.Log($"[Orchestrator] Reset first action flags for {resetCount} units");
        }

        /// <summary>
        /// 사망한 유닛의 상태 정리 (메모리 누수 방지)
        /// </summary>
        private static void CleanupDeadUnitStates()
        {
            try
            {
                var allUnits = Game.Instance?.State?.AllBaseAwakeUnits;
                if (allUnits == null) return;

                // 살아있는 유닛 ID 수집
                var aliveUnitIds = new HashSet<string>();
                foreach (var unit in allUnits)
                {
                    if (unit != null && !unit.LifeState.IsDead)
                    {
                        aliveUnitIds.Add(unit.UniqueId);
                    }
                }

                // 죽은 유닛의 상태 제거
                var keysToRemove = new List<string>();
                foreach (var key in _turnStates.Keys)
                {
                    if (!aliveUnitIds.Contains(key))
                    {
                        keysToRemove.Add(key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _turnStates.Remove(key);
                }

                if (keysToRemove.Count > 0)
                {
                    Main.LogDebug($"[Orchestrator] Cleaned up {keysToRemove.Count} dead unit states");
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[Orchestrator] CleanupDeadUnitStates error: {ex}");
            }
        }

        private static TurnState GetOrCreateTurnState(BaseUnitEntity unit)
        {
            string key = unit.UniqueId;
            if (!_turnStates.TryGetValue(key, out var state))
            {
                state = new TurnState();
                _turnStates[key] = state;
            }
            return state;
        }

        public static void OnCombatStart()
        {
            _turnStates.Clear();
            _lastKnownRound = 0;
            _combatStartFrame = UnityEngine.Time.frameCount;
            Main.Log("[Orchestrator] Combat started - states cleared");
        }

        public static void OnCombatEnd()
        {
            _turnStates.Clear();
            _lastKnownRound = 0;
            _combatStartFrame = 0;
            Main.Log("[Orchestrator] Combat ended - states cleared");
        }

        /// <summary>
        /// 유닛 사망 시 호출 - 즉시 상태 정리
        /// </summary>
        public static void OnUnitDeath(BaseUnitEntity unit)
        {
            if (unit == null) return;

            string key = unit.UniqueId;
            if (_turnStates.ContainsKey(key))
            {
                _turnStates.Remove(key);
                Main.LogDebug($"[Orchestrator] Removed state for dead unit: {unit.CharacterName}");
            }
        }

        #endregion

        #region HP Cost Safety

        private static (AbilityData ability, TargetWrapper target) TryPreventUnsafeAbilityDelegation(ActionContext ctx)
        {
            if (ctx.HPPercent > HP_COST_ABILITY_THRESHOLD)
                return (null, null);

            bool hasHPCostAbility = false;
            AbilityData safeAbility = null;

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (GameAPI.IsHPCostAbility(ability))
                {
                    hasHPCostAbility = true;
                }
                else if (safeAbility == null && GameAPI.IsOffensiveAbility(ability))
                {
                    safeAbility = ability;
                }
            }

            if (!hasHPCostAbility)
                return (null, null);

            if (safeAbility != null && ctx.NearestEnemy != null)
            {
                var targetWrapper = new TargetWrapper(ctx.NearestEnemy);
                string reason;
                if (GameAPI.CanUseAbilityOn(safeAbility, targetWrapper, out reason))
                {
                    Main.Log($"[Orchestrator] HP low ({ctx.HPPercent:F0}%), using safe ability: {safeAbility.Name}");
                    return (safeAbility, targetWrapper);
                }
            }

            return (null, null);
        }

        #endregion
    }
}
