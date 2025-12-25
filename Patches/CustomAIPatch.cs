using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.AI;
using Kingmaker.AI.BehaviourTrees;
using Kingmaker.AI.BehaviourTrees.Nodes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2_2.Core;
using CompanionAI_v2_2.Settings;

namespace CompanionAI_v2_2.Patches
{
    /// <summary>
    /// v2.2.0: 커스텀 AI 패치 - 타이밍 인식 시스템
    /// v2.2.14: TaskNodeCastAbility 패치로 능력 실행 보장
    /// v2.2.15: 복원 후 즉시 추적 초기화 (무한 루프 방지)
    /// v2.2.16: 검증 실패한 공격 강제 금지 - 게임 AI에 위임
    /// v2.2.17: 실패한 공격 타겟 블랙리스트 - 무한 루프 방지
    /// v2.2.20: MoveAndCastStrategy 트리거 - Ability만 설정하고 Target=null로 Failure 반환
    /// v2.2.21: AfterMove 감지 - 이동 후에는 게임 AI가 타겟 선택
    /// v2.2.22: IsMovementInfluentAbility 플래그 - ToClosestEnemy 폴백 방지
    /// v2.2.23: TaskNodeSetupMoveCommand 패치 - ToClosestEnemy 차단
    /// v2.2.24: 메디킷 사용 조건 강화 - HP 50% 이하일 때만 사용
    /// v2.2.25: 메디킷 GUID 기반 감지 + Dead code 정리
    /// v2.2.33: ★ 첫 번째 결정 기회에서 항상 Orchestrator 실행!
    ///          ToBetterPosition으로 이동하는 캐릭터(아르젠타 등)가 제어 안 되던 문제 수정
    ///
    /// TaskNodeSelectAbilityTarget.TickInternal을 가로채서
    /// 커스텀 AI 로직으로 대체하고, TaskNodeCastAbility에서 확실히 실행
    ///
    /// ★ 핵심 발견: 게임 행동 트리 구조
    /// - Ability=null + Failure → hideAwayStrategy (숨기만 함)
    /// - Ability!=null + Failure → MoveAndCastStrategy (이동 후 공격!)
    /// - IsMovementInfluentAbility=true → TaskNodeFindBetterPlace (AttackEffectivenessTileScorer)
    /// - IsMovementInfluentAbility=false → LoopOverAbilities → ToClosestEnemy 폴백
    /// </summary>
    public static class CustomAIPatch
    {
        // ★ v2.2.14: 현재 턴에 우리 모드가 선택한 능력/타겟 저장
        private static AbilityData _selectedAbility = null;
        private static TargetWrapper _selectedTarget = null;
        private static string _selectedUnitId = null;

        // ★ v2.2.17: 실패한 공격 타겟 블랙리스트 (턴당)
        private static HashSet<string> _failedTargets = new HashSet<string>();
        private static int _consecutiveFailures = 0;
        private const int MAX_FAILURES_BEFORE_DELEGATE = 2;

        // ★ v2.2.17: 마지막 결정 추적 (동일 결정 반복 감지)
        private static string _lastDecisionKey = null;
        private static int _lastDecisionFrame = 0;

        // ★ v2.2.21: CastTimepointType 리플렉션 캐시
        private static FieldInfo _castTimepointField = null;

        #region Main Patch - TaskNodeSelectAbilityTarget

        [HarmonyPatch(typeof(TaskNodeSelectAbilityTarget))]
        public static class SelectAbilityTargetPatch
        {
            [HarmonyPatch("TickInternal")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.High)]
            static bool Prefix(TaskNodeSelectAbilityTarget __instance, Blackboard blackboard, ref Status __result)
            {
                try
                {
                    var context = blackboard?.DecisionContext;
                    if (context == null) return true;

                    var unit = context.Unit;
                    if (unit == null || !unit.IsDirectlyControllable) return true;

                    // 설정 확인
                    var settings = Main.Settings?.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
                    if (settings == null || !settings.EnableCustomAI) return true;

                    // ★ v2.2.33: CastTimepointType 가져오기
                    int castTimepoint = GetCastTimepoint(__instance);
                    bool isAfterMove = (castTimepoint != 0);  // 0 = BeforeMove, 1 = AfterMove, 2 = None, etc.

                    // ★ v2.2.33: 첫 번째 결정 기회인지 확인
                    // 이전 문제: AfterMove를 무조건 스킵 → ToBetterPosition으로 이동하는 캐릭터가 제어 안 됨
                    // 해결: 첫 번째 결정 기회에서는 CastTimepoint 무관하게 항상 Orchestrator 실행!
                    bool isNewDecision = (_selectedUnitId != unit.UniqueId);

                    Main.Log($"[v2.2.33] {unit.CharacterName}: CastTimepoint={castTimepoint}, IsAfterMove={isAfterMove}, IsNewDecision={isNewDecision}");

                    // ★ v2.2.33: 이미 이 유닛에 대해 결정을 내린 상태에서 AfterMove면 → 게임 AI에 위임
                    // (우리 결정 후 이동해서 타겟 선택하는 경우 등)
                    if (!isNewDecision && isAfterMove)
                    {
                        Main.Log($"[v2.2.33] {unit.CharacterName}: Already decided + AfterMove - letting game AI select target");
                        return true;  // 원본 메서드 실행!
                    }

                    // ★ v2.2.33: 새 결정이면 상태 초기화
                    if (isNewDecision)
                    {
                        _failedTargets.Clear();
                        _consecutiveFailures = 0;
                        _lastDecisionKey = null;
                        _lastDecisionFrame = 0;
                    }

                    // ★ v2.2.33: Orchestrator 실행! (첫 번째 결정 OR BeforeMove)
                    __result = ExecuteCustomLogic(context, unit, settings);
                    return false;  // 원본 메서드 실행 안함
                }
                catch (Exception ex)
                {
                    Main.LogError($"[Patch] SelectAbilityTarget error: {ex}");
                    return true;  // 오류 시 원본 실행
                }
            }

            /// <summary>
            /// ★ v2.2.21: CastTimepointType 값 가져오기 (리플렉션)
            /// </summary>
            private static int GetCastTimepoint(TaskNodeSelectAbilityTarget instance)
            {
                try
                {
                    if (_castTimepointField == null)
                    {
                        _castTimepointField = typeof(TaskNodeSelectAbilityTarget)
                            .GetField("m_CastTimepoint", BindingFlags.NonPublic | BindingFlags.Instance);
                    }

                    if (_castTimepointField != null)
                    {
                        var value = _castTimepointField.GetValue(instance);
                        return (int)value;  // CastTimepointType enum to int
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[v2.2.21] GetCastTimepoint error: {ex.Message}");
                }
                return 0;  // 기본값: BeforeMove
            }

            private static Status ExecuteCustomLogic(
                DecisionContext context,
                BaseUnitEntity unit,
                CharacterSettings settings)
            {
                // 초기화
                context.AbilityTarget = null;
                context.Ability = null;

                // ★ v2.2.17: 연속 실패 횟수 체크 - 너무 많으면 게임 AI에 위임
                if (_consecutiveFailures >= MAX_FAILURES_BEFORE_DELEGATE)
                {
                    Main.Log($"[v2.2.17] {unit.CharacterName}: {_consecutiveFailures} consecutive failures - delegating to game AI");
                    _consecutiveFailures = 0;  // 리셋
                    return Status.Failure;
                }

                // Orchestrator 호출
                var (ability, target) = AIOrchestrator.DecideAction(context, unit, settings);

                if (ability != null && target != null)
                {
                    string targetKey = GetTargetKey(ability, target);
                    int currentFrame = UnityEngine.Time.frameCount;

                    // ★ v2.2.17: 이 타겟이 블랙리스트에 있는지 확인
                    if (_failedTargets.Contains(targetKey))
                    {
                        Main.Log($"[v2.2.17] {unit.CharacterName}: Skipping blacklisted target {GetTargetName(target)}");
                        _consecutiveFailures++;
                        // ★ v2.2.20: 블랙리스트된 타겟 → MoveAndCast 트리거
                        return TriggerMoveAndCast(context, unit, ability);
                    }

                    // ★ v2.2.17: 동일한 결정이 짧은 시간 내에 반복되면 이전 시도가 실패한 것
                    // 블랙리스트에 추가하고 다른 타겟 시도
                    if (_lastDecisionKey == targetKey && (currentFrame - _lastDecisionFrame) < 30)
                    {
                        Main.Log($"[v2.2.17] {unit.CharacterName}: Same decision repeated (frame {_lastDecisionFrame} -> {currentFrame}) - blacklisting {GetTargetName(target)}");
                        _failedTargets.Add(targetKey);
                        _consecutiveFailures++;
                        _lastDecisionKey = null;
                        // ★ v2.2.20: 반복 결정 → MoveAndCast 트리거
                        return TriggerMoveAndCast(context, unit, ability);
                    }

                    context.Ability = ability;
                    context.AbilityTarget = target;

                    // ★ v2.2.14: 선택한 능력/타겟 저장 (CastAbility에서 복원용)
                    _selectedAbility = ability;
                    _selectedTarget = target;
                    _selectedUnitId = unit.UniqueId;

                    // ★ v2.2.17: 이 결정 기록
                    _lastDecisionKey = targetKey;
                    _lastDecisionFrame = currentFrame;

                    Main.Log($"[v2.2.17] DECISION: {unit.CharacterName} uses {ability.Name} -> {GetTargetName(target)} (frame {currentFrame})");
                    return Status.Success;
                }

                // ★ v2.2.20: 능력은 있지만 타겟이 없음 (현재 위치에서 공격 불가)
                // → MoveAndCastStrategy 트리거!
                if (ability != null && target == null)
                {
                    Main.Log($"[v2.2.20] {unit.CharacterName}: Has {ability.Name} but no target - triggering MoveAndCast");
                    return TriggerMoveAndCast(context, unit, ability);
                }

                // ★ v2.2.20: 결정 완전 실패 - 공격 능력이라도 찾아서 MoveAndCast 시도
                var fallbackAbility = GameAPI.FindAnyAttackAbility(unit);
                if (fallbackAbility != null)
                {
                    Main.Log($"[v2.2.20] {unit.CharacterName}: No decision, using fallback {fallbackAbility.Name} for MoveAndCast");
                    return TriggerMoveAndCast(context, unit, fallbackAbility);
                }

                // 진짜 할 게 없음 - hideAwayStrategy 또는 턴 종료
                Main.LogDebug($"[v2.2] {unit.CharacterName}: No ability at all, delegating to game AI");
                return Status.Failure;
            }

            /// <summary>
            /// ★ v2.2.20: MoveAndCastStrategy 트리거
            /// ★ v2.2.22: IsMovementInfluentAbility 플래그 설정 - TaskNodeFindBetterPlace 사용하도록!
            /// 핵심: Ability만 설정하고 Target은 null로 두고 Failure 반환
            /// → 게임 행동 트리에서 Ability != null 이면 MoveAndCastStrategy 실행!
            /// </summary>
            private static Status TriggerMoveAndCast(DecisionContext context, BaseUnitEntity unit, AbilityData ability)
            {
                context.Ability = ability;
                context.AbilityTarget = null;  // 타겟 없음!

                // ★ v2.2.22: 이 플래그가 없으면 MovementDecisionSubtree가 ToClosestEnemy 폴백으로 감!
                // IsMovementInfluentAbility = true → TaskNodeFindBetterPlace 사용 (AttackEffectivenessTileScorer)
                // IsMovementInfluentAbility = false → LoopOverAbilities → 실패시 ToClosestEnemy
                context.IsMovementInfluentAbility = true;

                Main.Log($"[v2.2.22] MOVE_AND_CAST: {unit.CharacterName} will move then use {ability.Name} (IsMovementInfluentAbility=true)");

                // Failure 반환하지만 Ability가 설정되어 있으므로:
                // → Condition(Ability == null) → FALSE → hideAwayStrategy 건너뜀
                // → Condition(Ability != null) → TRUE → MoveAndCastStrategy 실행!
                return Status.Failure;
            }

            private static string GetTargetKey(AbilityData ability, TargetWrapper target)
            {
                string abilityId = ability?.Blueprint?.AssetGuid?.ToString() ?? "unknown";
                string targetId = "unknown";
                if (target?.Entity is BaseUnitEntity targetUnit)
                    targetId = targetUnit.UniqueId;
                else if (target?.Point != UnityEngine.Vector3.zero)
                    targetId = $"point_{target.Point.x:F0}_{target.Point.z:F0}";
                return $"{abilityId}_{targetId}";
            }

            private static string GetTargetName(TargetWrapper target)
            {
                if (target == null) return "null";
                if (target.Entity is BaseUnitEntity targetUnit)
                    return targetUnit.CharacterName;
                if (target.Point != UnityEngine.Vector3.zero)
                    return $"Point({target.Point.x:F1}, {target.Point.z:F1})";
                return "Unknown";
            }
        }

        #endregion

        #region ★ v2.2.14: TaskNodeCastAbility 패치 - 능력 실행 보장

        /// <summary>
        /// TaskNodeCastAbility가 실행되기 전에 우리가 선택한 능력/타겟으로 복원
        /// LoopOverAbilities 등이 덮어썼을 수 있음
        /// </summary>
        [HarmonyPatch(typeof(TaskNodeCastAbility))]
        public static class CastAbilityPatch
        {
            // ★ v2.2.25: 모든 메디킷 능력 GUID (언어 독립적)
            private static readonly HashSet<string> MedikitGUIDs = new HashSet<string>
            {
                "083d5280759b4ed3a2d0b61254653273",  // Medikit_ability (기본 메디킷)
                "d722bfac662c40f9b2a47dc6ea70d00a",  // LargeMedikit_ability (대형 메디킷)
                "489842740f8d45d4bd5f27c44a89fae1",  // UpgradedMedikit_ability (고급 메디킷)
                "49a5f617380f43e2a44e5774e97cd076",  // OfficersMedikit_ability (장교용 메디킷)
                "ededbc48a7f24738a0fdb708fc48bb4c",  // ChirurgeonMedikit_Ability (군의관 메디킷)
                "b6e3c9398ea94c75afdbf61633ce2f85",  // BattleMedicsBoots_Medikit_ability
                "dd2e9a6170b448d4b2ec5a7fe0321e65",  // BattleStimulator_Medikit_ability (전투 자극제 메디킷)
                "2e9a23383b574408b4acdf6b62f6ed9b",  // HumanRavor_Medikit_Ability (레이버의 메디킷)
                "4515c4b5205d4c30b3346f39a2039953",  // MedikitMobAbility
                "c8a3291a10ad4cc1a38228db11dbc389",  // InquisitorBoarding_Medikit_Ability (이단심문소 메디킷)
            };

            [HarmonyPatch("CreateCoroutine")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.High)]
            static bool CreateCoroutine_Prefix(Blackboard blackboard, ref IEnumerator<Status> __result)
            {
                try
                {
                    var context = blackboard?.DecisionContext;
                    if (context == null) return true;

                    var unit = context.Unit;
                    if (unit == null) return true;

                    // ★ v2.2.24: 메디킷 사용 조건 확인 (게임 AI 결정도 차단)
                    if (unit.IsDirectlyControllable && context.Ability != null)
                    {
                        var settings = Main.Settings?.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
                        if (settings != null && settings.EnableCustomAI)
                        {
                            if (ShouldBlockMedikit(context, unit))
                            {
                                // 메디킷 사용 차단 - 빈 코루틴 반환
                                __result = EmptyCoroutine();
                                return false;
                            }
                        }
                    }

                    // 우리 모드가 선택한 유닛인지 확인 (기존 복원 로직)
                    if (_selectedUnitId != unit.UniqueId) return true;
                    if (_selectedAbility == null || _selectedTarget == null) return true;

                    // 현재 context의 능력이 다르면 복원
                    var currentAbilityGuid = context.Ability?.Blueprint?.AssetGuid?.ToString();
                    var selectedAbilityGuid = _selectedAbility.Blueprint?.AssetGuid?.ToString();

                    if (currentAbilityGuid != selectedAbilityGuid)
                    {
                        Main.Log($"[v2.2.15] RESTORE: {context.Ability?.Name ?? "null"} -> {_selectedAbility.Name}");
                        context.Ability = _selectedAbility;
                        context.AbilityTarget = _selectedTarget;

                        // ★ v2.2.15: 복원 후 즉시 추적 초기화 (무한 루프 방지)
                        ClearTracking();
                    }
                    else if (context.AbilityTarget == null && _selectedTarget != null)
                    {
                        Main.Log($"[v2.2.15] RESTORE TARGET: {_selectedAbility.Name}");
                        context.AbilityTarget = _selectedTarget;
                        ClearTracking();
                    }
                    else
                    {
                        // 능력이 일치하면 추적 초기화 (정상 실행)
                        ClearTracking();
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[Patch] CastAbility_Prefix error: {ex.Message}");
                }

                return true;
            }

            /// <summary>
            /// ★ v2.2.25: 메디킷 사용을 차단해야 하는지 확인 (GUID 기반)
            /// ★ v2.2.27: HealAtHPPercent 설정 반영
            /// </summary>
            private static bool ShouldBlockMedikit(DecisionContext context, BaseUnitEntity caster)
            {
                var ability = context.Ability;
                if (ability == null) return false;

                // ★ v2.2.25: GUID로 메디킷인지 확인 (언어 독립적)
                string abilityGuid = ability.Blueprint?.AssetGuid?.ToString();
                if (string.IsNullOrEmpty(abilityGuid) || !MedikitGUIDs.Contains(abilityGuid))
                    return false;

                // 타겟 확인
                var target = context.AbilityTarget?.Entity as BaseUnitEntity;
                if (target == null) return false;

                // 타겟의 HP 확인
                var health = target.Health;
                if (health == null) return false;

                float hpPercent = (float)health.HitPointsLeft / health.MaxHitPoints;

                // ★ v2.2.27: 설정에서 힐 임계값 가져오기 (기본 50%)
                var settings = Main.Settings?.GetOrCreateSettings(caster.UniqueId, caster.CharacterName);
                float threshold = (settings?.HealAtHPPercent ?? 50) / 100f;

                // HP가 임계값 이상이면 차단
                if (hpPercent >= threshold)
                {
                    Main.Log($"[v2.2.27] {caster.CharacterName}: BLOCKED Medikit on {target.CharacterName} (HP={hpPercent:P0} >= {threshold:P0})");
                    return true;
                }

                return false;
            }

            /// <summary>
            /// 빈 코루틴 (Failure 반환)
            /// </summary>
            private static IEnumerator<Status> EmptyCoroutine()
            {
                yield return Status.Failure;
            }
        }

        /// <summary>
        /// 캐스트 시도 후 선택 정보만 초기화
        /// ★ v2.2.17 수정: _lastDecisionKey와 _lastDecisionFrame은 유지!
        /// 반복 결정 감지를 위해 필요 (CastAbility 전에 호출되기 때문)
        /// </summary>
        public static void ClearTracking()
        {
            _selectedAbility = null;
            _selectedTarget = null;
            _selectedUnitId = null;
            // ★ v2.2.17: 아래 변수들은 유지! 반복 감지에 필요
            // _consecutiveFailures - 유지
            // _lastDecisionKey - 유지
            // _lastDecisionFrame - 유지
        }

        /// <summary>
        /// ★ v2.2.17: 턴 시작 시 블랙리스트 초기화
        /// </summary>
        public static void OnTurnStart()
        {
            _failedTargets.Clear();
            _consecutiveFailures = 0;
            _lastDecisionKey = null;
            _lastDecisionFrame = 0;
            ClearTracking();
        }

        /// <summary>
        /// ★ v2.2.17: 전투 시작/종료 시 모든 상태 초기화
        /// </summary>
        public static void OnCombatStateChanged()
        {
            _failedTargets.Clear();
            _consecutiveFailures = 0;
            _lastDecisionKey = null;
            _lastDecisionFrame = 0;
            ClearTracking();
            Main.LogDebug("[v2.2.17] Combat state changed - all tracking reset");
        }

        #endregion

        #region ★ v2.2.23: TaskNodeSetupMoveCommand 패치 - ToClosestEnemy 차단!

        /// <summary>
        /// ★ v2.2.23: 핵심 패치!
        /// 원거리 플레이어 컴패니언이 ToClosestEnemy로 적에게 돌진하는 것을 방지
        ///
        /// 문제: MovementDecisionSubtree에서 IsMovementInfluentAbility 조건이 실패하면
        ///       LoopOverAbilities → ToClosestEnemy 폴백으로 적에게 달려감!
        ///
        /// 해결: TaskNodeSetupMoveCommand.TickInternal에서 ClosestEnemy 모드일 때
        ///       원거리 플레이어 컴패니언은 이동하지 않도록 차단 (Failure 반환)
        ///       → 이동 없이 현재 위치에서 공격 시도 또는 턴 종료
        /// </summary>
        [HarmonyPatch(typeof(TaskNodeSetupMoveCommand))]
        public static class SetupMoveCommandPatch
        {
            // SetupMoveCommandMode enum 값
            private const int MODE_CLOSEST_ENEMY = 1;  // ClosestEnemy

            // m_Mode 필드 캐시
            private static FieldInfo _modeField = null;

            [HarmonyPatch("TickInternal")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.High)]
            static bool Prefix(TaskNodeSetupMoveCommand __instance, Blackboard blackboard, ref Status __result)
            {
                try
                {
                    var context = blackboard?.DecisionContext;
                    if (context == null) return true;

                    var unit = context.Unit;
                    if (unit == null || !unit.IsDirectlyControllable) return true;

                    // 설정 확인
                    var settings = Main.Settings?.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
                    if (settings == null || !settings.EnableCustomAI) return true;

                    // m_Mode 가져오기
                    if (_modeField == null)
                    {
                        _modeField = typeof(TaskNodeSetupMoveCommand)
                            .GetField("m_Mode", BindingFlags.NonPublic | BindingFlags.Instance);
                    }

                    if (_modeField == null) return true;

                    int mode = (int)_modeField.GetValue(__instance);

                    // ClosestEnemy 모드인지 확인
                    if (mode != MODE_CLOSEST_ENEMY) return true;

                    // ★ v2.2.23: 원거리 무기를 가지고 있는지 확인
                    // IsUsualMeleeUnit은 첫 번째 무기만 체크하므로 부정확!
                    // 파스칼처럼 액스 + 플라스마건 조합이면 IsUsualMeleeUnit = true가 됨
                    bool hasRangedWeapon = HasRangedWeapon(unit);

                    if (hasRangedWeapon)
                    {
                        // ★ 원거리 무기가 있으면 ToClosestEnemy로 이동하지 않음!
                        // Failure 반환 → 이동 없이 다음 로직으로
                        Main.Log($"[v2.2.23] {unit.CharacterName}: BLOCKED ToClosestEnemy (has ranged weapon) - will attack from current position");
                        __result = Status.Failure;
                        return false;  // 원본 실행 안함
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[Patch] SetupMoveCommand error: {ex.Message}");
                }

                return true;  // 원본 실행
            }

            /// <summary>
            /// 원거리 무기를 가지고 있는지 확인
            /// </summary>
            private static bool HasRangedWeapon(BaseUnitEntity unit)
            {
                try
                {
                    // 주 무기 확인
                    var primaryHand = unit.Body?.PrimaryHand;
                    if (primaryHand?.HasWeapon == true)
                    {
                        var weapon = primaryHand.Weapon;
                        if (weapon?.Blueprint != null && !weapon.Blueprint.IsMelee)
                            return true;
                    }

                    // 보조 무기 확인
                    var secondaryHand = unit.Body?.SecondaryHand;
                    if (secondaryHand?.HasWeapon == true)
                    {
                        var weapon = secondaryHand.Weapon;
                        if (weapon?.Blueprint != null && !weapon.Blueprint.IsMelee)
                            return true;
                    }

                    // 보조 무기 세트 확인 (Secondary HandsEquipmentSet)
                    var altPrimaryHand = unit.Body?.HandsEquipmentSets?.LastOrDefault()?.PrimaryHand;
                    if (altPrimaryHand?.HasWeapon == true)
                    {
                        var weapon = altPrimaryHand.Weapon;
                        if (weapon?.Blueprint != null && !weapon.Blueprint.IsMelee)
                            return true;
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[HasRangedWeapon] Error: {ex.Message}");
                }

                return false;
            }
        }

        #endregion
    }
}
