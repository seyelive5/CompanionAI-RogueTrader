using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Kingmaker.AI;
using Kingmaker.AI.AreaScanning;
using Kingmaker.AI.BehaviourTrees;
using Kingmaker.AI.BehaviourTrees.Nodes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Pathfinding;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using Pathfinding;
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

        // ★ v2.2.55: 무한 루프 방지 - 액션 카운터 (폴백용)
        private static int _actionCounter = 0;
        private static string _actionCounterUnitId = null;
        private const int MAX_ACTIONS_PER_TURN = 20;  // 안전장치 (정상적으로는 도달 안 함)

        // ★ v2.2.61: _attemptedAbilities 제거됨
        // 이유: _lastDecisionKey가 이미 ability+target 조합을 추적하므로 중복
        // 다른 안전장치: _failedTargets, _consecutiveFailures, _actionCounter

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

                // ★ v2.2.55: 유닛 변경 시 액션 카운터 초기화
                if (_actionCounterUnitId != unit.UniqueId)
                {
                    _actionCounter = 0;
                    _actionCounterUnitId = unit.UniqueId;
                }
                _actionCounter++;

                // ★ v2.2.55: 안전장치 - 비정상적으로 많은 액션 (정상적으로는 도달 안 함)
                if (_actionCounter > MAX_ACTIONS_PER_TURN)
                {
                    Main.Log($"[v2.2.55] {unit.CharacterName}: Safety limit ({_actionCounter}) → force end turn");
                    return Status.Failure;
                }

                // ★ v2.2.17: 연속 실패 횟수 체크 - 너무 많으면 게임 AI에 위임
                if (_consecutiveFailures >= MAX_FAILURES_BEFORE_DELEGATE)
                {
                    Main.Log($"[v2.2.17] {unit.CharacterName}: {_consecutiveFailures} consecutive failures - delegating to game AI");
                    _consecutiveFailures = 0;  // 리셋
                    return Status.Failure;
                }

                // ★ v2.2.49: 이동 우선 체크 - PreferRanged 유닛의 주 무기가 타겟 없으면 이동 먼저!
                // 문제: BeforeMove에서 사이킥 스킬(긴 사거리) 선택 → 공격 → 이동 없이 AP 소진
                // 해결: 주 무기로 공격 불가능하면 MoveAndCast 트리거 → 이동 후 공격
                var moveFirstResult = TryTriggerMovementFirst(context, unit, settings);
                if (moveFirstResult.HasValue)
                {
                    return moveFirstResult.Value;
                }

                // Orchestrator 호출
                var (ability, target) = AIOrchestrator.DecideAction(context, unit, settings);

                if (ability != null && target != null)
                {
                    string targetKey = GetTargetKey(ability, target);
                    int currentFrame = UnityEngine.Time.frameCount;

                    // ★ v2.2.61: _attemptedAbilities 체크 제거됨
                    // _lastDecisionKey가 ability+target 조합으로 반복 감지하므로 불필요
                    // 덕분에 같은 스킬을 다른 타겟에 여러 번 사용 가능!

                    // ★ v2.2.17: 이 타겟이 블랙리스트에 있는지 확인
                    if (_failedTargets.Contains(targetKey))
                    {
                        Main.Log($"[v2.2.17] {unit.CharacterName}: Skipping blacklisted target {GetTargetName(target)}");
                        _consecutiveFailures++;
                        // ★ v2.2.20: 블랙리스트된 타겟 → MoveAndCast 트리거
                        // ★ v2.2.41: settings 전달 - 하드 필터 적용
                        return TriggerMoveAndCast(context, unit, ability, settings);
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
                        // ★ v2.2.41: settings 전달 - 하드 필터 적용
                        return TriggerMoveAndCast(context, unit, ability, settings);
                    }

                    context.Ability = ability;
                    context.AbilityTarget = target;

                    // ★ v2.2.44: PreferRanged 유닛은 IsMovementInfluentAbility 설정!
                    // 타겟이 사거리 밖이면 게임이 ToClosestEnemy 대신 TaskNodeFindBetterPlace 사용
                    // → 우리의 FindBetterPlacePatch가 적절한 위치 결정
                    var pref = settings?.RangePreference ?? RangePreference.Adaptive;
                    if (pref == RangePreference.PreferRanged || pref == RangePreference.MaintainRange)
                    {
                        context.IsMovementInfluentAbility = true;
                        Main.LogDebug($"[v2.2.44] {unit.CharacterName}: Set IsMovementInfluentAbility=true for ranged preference");
                    }

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
                // ★ v2.2.41: settings 전달 - 하드 필터 적용
                if (ability != null && target == null)
                {
                    Main.Log($"[v2.2.20] {unit.CharacterName}: Has {ability.Name} but no target - triggering MoveAndCast");
                    return TriggerMoveAndCast(context, unit, ability, settings);
                }

                // ★ v2.2.36: 후퇴가 필요한 경우 HideAwayStrategy 트리거
                // 조건: 원거리 선호 + 적이 가까움 + 공격 불가
                // Ability=null + Failure → 게임 AI의 HideAwayStrategy가 엄폐 위치로 이동
                if (ShouldTriggerHideAwayStrategy(unit, settings))
                {
                    Main.Log($"[v2.2.36] {unit.CharacterName}: Triggering HideAwayStrategy for retreat/cover");
                    // Ability=null 상태에서 Failure 반환 → HideAwayStrategy
                    return Status.Failure;
                }

                // ★ v2.2.20: 결정 완전 실패 - 공격 능력이라도 찾아서 MoveAndCast 시도
                // ★ v2.2.40: RangePreference 전달 - 선호 무기 타입 우선
                // ★ v2.2.41: 하드 필터는 TriggerMoveAndCast 내에서 적용됨
                var rangePreference = settings?.RangePreference ?? Settings.RangePreference.Adaptive;
                var fallbackAbility = GameAPI.FindAnyAttackAbility(unit, rangePreference);
                if (fallbackAbility != null)
                {
                    Main.LogDebug($"[v2.2.41] {unit.CharacterName}: Fallback with {rangePreference} → {fallbackAbility.Name}");
                    return TriggerMoveAndCast(context, unit, fallbackAbility, settings);
                }

                // 진짜 할 게 없음 - hideAwayStrategy 또는 턴 종료
                Main.LogDebug($"[v2.2] {unit.CharacterName}: No ability at all, delegating to game AI");
                return Status.Failure;
            }

            /// <summary>
            /// ★ v2.2.20: MoveAndCastStrategy 트리거
            /// ★ v2.2.22: IsMovementInfluentAbility 플래그 설정 - TaskNodeFindBetterPlace 사용하도록!
            /// ★ v2.2.41: 하드 필터 - PreferRanged인데 근접 공격이면 HideAwayStrategy로 대체!
            /// ★ v2.2.54: MP=0이면 MoveAndCast 무의미 → 즉시 턴 종료!
            /// 핵심: Ability만 설정하고 Target은 null로 두고 Failure 반환
            /// → 게임 행동 트리에서 Ability != null 이면 MoveAndCastStrategy 실행!
            /// </summary>
            private static Status TriggerMoveAndCast(DecisionContext context, BaseUnitEntity unit, AbilityData ability, CharacterSettings settings = null)
            {
                // ★ v2.2.54: MP=0이면 이동 불가 → MoveAndCast 무의미 → 즉시 턴 종료!
                // 이전 문제: MP=0인데도 MoveAndCast 반복 시도 → 100번+ 루프 → 40초 타임아웃
                float availableMP = unit.CombatState?.ActionPointsBlue ?? 0f;
                if (availableMP <= 0.1f)  // 이동에 필요한 최소 AP도 없음
                {
                    Main.Log($"[v2.2.54] {unit.CharacterName}: MP=0 - cannot move, skipping MoveAndCast → end turn");
                    context.Ability = null;  // Ability=null → 게임이 턴 종료 처리
                    return Status.Failure;
                }

                // ★ v2.2.41: 하드 필터 - PreferRanged인데 근접 공격이면 차단
                if (settings != null)
                {
                    var rangePreference = settings.RangePreference;
                    bool isPreferredWeapon = CombatHelpers.IsPreferredWeaponType(ability, rangePreference);

                    if (!isPreferredWeapon &&
                        (rangePreference == Settings.RangePreference.PreferRanged ||
                         rangePreference == Settings.RangePreference.MaintainRange))
                    {
                        Main.Log($"[v2.2.41] {unit.CharacterName}: BLOCKED MoveAndCast - {ability.Name} violates {rangePreference}! → HideAwayStrategy");
                        // Ability=null + Failure → HideAwayStrategy로 엄폐 이동
                        context.Ability = null;
                        return Status.Failure;
                    }
                }

                context.Ability = ability;
                context.AbilityTarget = null;  // 타겟 없음!

                // ★ v2.2.22: 이 플래그가 없으면 MovementDecisionSubtree가 ToClosestEnemy 폴백으로 감!
                // IsMovementInfluentAbility = true → TaskNodeFindBetterPlace 사용 (AttackEffectivenessTileScorer)
                // IsMovementInfluentAbility = false → LoopOverAbilities → 실패시 ToClosestEnemy
                context.IsMovementInfluentAbility = true;

                Main.Log($"[v2.2.41] MOVE_AND_CAST: {unit.CharacterName} will move then use {ability.Name} (IsMovementInfluentAbility=true)");

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

            /// <summary>
            /// ★ v2.2.49: 이동 우선 체크 - PreferRanged 유닛의 주 무기가 타겟 없으면 이동 먼저!
            /// ★ v2.2.57: Hittable 체크 사용! 거리만으로는 부족 (LOS, 엄폐 등 고려 안 됨)
            ///
            /// 문제 상황 (v2.2.52):
            /// - 거리 체크: 6.0 <= 10 → "in range"로 판단
            /// - 실제 Hittable: TargetTooFar → 공격 불가!
            /// - 결과: 이동 안 함 → 공격 불가 상태에서 시간 낭비
            ///
            /// v2.2.57 수정: 게임의 IsHittable 체크 사용 → 진짜 공격 가능한 타겟만 인정
            ///
            /// 해결: 주 무기로 Hittable한 타겟 없음 + 첫 행동 + MP 충분 → 이동 먼저!
            /// </summary>
            private static Status? TryTriggerMovementFirst(DecisionContext context, BaseUnitEntity unit, CharacterSettings settings)
            {
                try
                {
                    // 1. PreferRanged/MaintainRange 설정 확인
                    var pref = settings?.RangePreference ?? RangePreference.Adaptive;
                    if (pref != RangePreference.PreferRanged && pref != RangePreference.MaintainRange)
                    {
                        return null;  // 원거리 선호 아님 → 이 체크 스킵
                    }

                    // 2. 첫 번째 행동인지 확인 (이미 스킬 사용했으면 스킵)
                    bool isFirstDecision = (_selectedUnitId != unit.UniqueId);
                    if (!isFirstDecision)
                    {
                        return null;  // 이미 결정함 → 스킵
                    }

                    // 3. MP가 충분한지 확인 (이동 가능해야 함)
                    float currentMP = unit.CombatState?.ActionPointsBlue ?? 0f;
                    if (currentMP < 1f)
                    {
                        return null;  // MP 부족 → 이동 불가 → 스킵
                    }

                    // 4. 원거리 무기 확인 - 원거리 무기가 있어야 이동 후 공격 의미 있음
                    var primaryRangedAttack = FindPrimaryRangedWeaponAttack(unit);
                    if (primaryRangedAttack == null)
                    {
                        return null;  // 원거리 주 무기 없음 → 스킵
                    }

                    // ★ v2.2.57: 게임의 Hittable 체크 사용!
                    // 거리 체크는 부정확 - LOS, 엄폐, 위협 범위 등 고려 안 됨
                    bool canAttackWithPrimaryNow = false;
                    var enemies = GameAPI.GetEnemies(unit);

                    if (enemies != null)
                    {
                        foreach (var enemy in enemies)
                        {
                            if (enemy == null || enemy.LifeState.IsDead) continue;

                            var targetWrapper = new TargetWrapper(enemy);

                            // ★ v2.2.57: CanUseAbilityOn 사용 - 게임과 동일한 기준 (LOS, Range 등)
                            string unavailableReason;
                            if (GameAPI.CanUseAbilityOn(primaryRangedAttack, targetWrapper, out unavailableReason))
                            {
                                canAttackWithPrimaryNow = true;
                                float distance = (unit.Position - enemy.Position).magnitude;
                                Main.LogDebug($"[v2.2.57] {unit.CharacterName}: {enemy.CharacterName} is HITTABLE ({distance:F1}m)");
                                break;
                            }
                        }
                    }

                    // 5. 주 무기로 Hittable한 타겟이 없으면 → 이동 먼저!
                    if (!canAttackWithPrimaryNow)
                    {
                        Main.Log($"[v2.2.57] ★ {unit.CharacterName}: Primary weapon ({primaryRangedAttack.Name}) has NO HITTABLE target - MOVE FIRST!");
                        return TriggerMoveAndCast(context, unit, primaryRangedAttack, settings);
                    }

                    // 주 무기로 공격 가능 → 정상 진행
                    Main.LogDebug($"[v2.2.57] {unit.CharacterName}: Primary weapon ({primaryRangedAttack.Name}) has hittable target - normal flow");
                    return null;
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[v2.2.57] TryTriggerMovementFirst error: {ex.Message}");
                    return null;
                }
            }

            /// <summary>
            /// ★ v2.2.49: 원거리 무기 공격 능력 찾기
            /// ★ v2.2.50: 무기 세트 지원 - Pascal처럼 근접+원거리 세트를 드는 캐릭터 지원
            /// </summary>
            private static AbilityData FindPrimaryRangedWeaponAttack(BaseUnitEntity unit)
            {
                try
                {
                    var abilities = unit.Abilities?.RawFacts;
                    if (abilities == null) return null;

                    // ★ v2.2.50: 모든 무기 슬롯에서 원거리 무기 찾기
                    var rangedWeapons = new List<Kingmaker.Items.ItemEntityWeapon>();

                    // 1. 주 무기
                    var primaryHand = unit.Body?.PrimaryHand;
                    if (primaryHand?.HasWeapon == true && primaryHand.Weapon?.Blueprint != null)
                    {
                        if (!primaryHand.Weapon.Blueprint.IsMelee)
                            rangedWeapons.Add(primaryHand.Weapon);
                    }

                    // 2. 보조 무기
                    var secondaryHand = unit.Body?.SecondaryHand;
                    if (secondaryHand?.HasWeapon == true && secondaryHand.Weapon?.Blueprint != null)
                    {
                        if (!secondaryHand.Weapon.Blueprint.IsMelee)
                            rangedWeapons.Add(secondaryHand.Weapon);
                    }

                    // 3. 다른 무기 세트
                    var handsSets = unit.Body?.HandsEquipmentSets;
                    if (handsSets != null)
                    {
                        foreach (var set in handsSets)
                        {
                            if (set?.PrimaryHand?.HasWeapon == true && set.PrimaryHand.Weapon?.Blueprint != null)
                            {
                                if (!set.PrimaryHand.Weapon.Blueprint.IsMelee && !rangedWeapons.Contains(set.PrimaryHand.Weapon))
                                    rangedWeapons.Add(set.PrimaryHand.Weapon);
                            }
                            if (set?.SecondaryHand?.HasWeapon == true && set.SecondaryHand.Weapon?.Blueprint != null)
                            {
                                if (!set.SecondaryHand.Weapon.Blueprint.IsMelee && !rangedWeapons.Contains(set.SecondaryHand.Weapon))
                                    rangedWeapons.Add(set.SecondaryHand.Weapon);
                            }
                        }
                    }

                    if (rangedWeapons.Count == 0)
                    {
                        Main.LogDebug($"[v2.2.50] {unit.CharacterName}: No ranged weapons found in any slot");
                        return null;
                    }

                    Main.LogDebug($"[v2.2.50] {unit.CharacterName}: Found {rangedWeapons.Count} ranged weapon(s)");

                    // 원거리 무기의 공격 능력 찾기
                    foreach (var weapon in rangedWeapons)
                    {
                        foreach (var ability in abilities)
                        {
                            var abilityData = ability?.Data;
                            if (abilityData == null) continue;

                            // 이 무기의 공격인지 확인
                            if (abilityData.Weapon == weapon)
                            {
                                // 사용 가능한지 확인
                                List<string> reasons;
                                if (GameAPI.IsAbilityAvailable(abilityData, out reasons))
                                {
                                    Main.LogDebug($"[v2.2.50] {unit.CharacterName}: Found ranged attack: {abilityData.Name}");
                                    return abilityData;
                                }
                            }
                        }
                    }

                    Main.LogDebug($"[v2.2.50] {unit.CharacterName}: No available ranged weapon attacks");
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[v2.2.50] FindPrimaryRangedWeaponAttack error: {ex.Message}");
                }

                return null;
            }

            /// <summary>
            /// ★ v2.2.36: HideAwayStrategy 트리거 여부 결정
            /// ★ v2.2.50: 무기 세트 지원 + 위협 범위 감지
            /// 조건: 원거리 선호 + (적이 가까움 OR 위협 범위 내)
            /// → 폴백 공격 대신 HideAwayStrategy로 엄폐 위치 이동
            /// </summary>
            private static bool ShouldTriggerHideAwayStrategy(BaseUnitEntity unit, CharacterSettings settings)
            {
                // 1. RangePreference 확인 - 원거리 선호가 아니면 HideAway 불필요
                var rangePreference = settings?.RangePreference ?? RangePreference.Adaptive;
                if (rangePreference != RangePreference.PreferRanged &&
                    rangePreference != RangePreference.MaintainRange)
                {
                    return false;
                }

                // ★ v2.2.50: 원거리 무기 확인 - 모든 슬롯/세트에서 확인
                bool hasRangedWeapon = HasAnyRangedWeapon(unit);
                if (!hasRangedWeapon) return false;

                // ★ v2.2.50: 위협 범위 내인지 확인 (CannotUseInThreatenedArea 방지)
                bool isInThreatenedArea = IsInThreatenedArea(unit);
                if (isInThreatenedArea)
                {
                    Main.Log($"[v2.2.50] {unit.CharacterName}: HideAway trigger (in threatened area - cannot use ranged)");
                    return true;
                }

                // 3. 가장 가까운 적과의 거리 확인
                float minSafeDistance = settings?.MinSafeDistance ?? 5f;
                float nearestEnemyDist = float.MaxValue;

                try
                {
                    var allUnits = Kingmaker.Game.Instance?.State?.AllBaseAwakeUnits;
                    if (allUnits != null)
                    {
                        foreach (var other in allUnits)
                        {
                            if (other == null || other == unit) continue;
                            if (other.LifeState.IsDead) continue;
                            if (!other.IsPlayerEnemy) continue;

                            float dist = UnityEngine.Vector3.Distance(unit.Position, other.Position);
                            if (dist < nearestEnemyDist) nearestEnemyDist = dist;
                        }
                    }
                }
                catch { }

                // 적이 가까움 → HideAwayStrategy 트리거
                if (nearestEnemyDist <= minSafeDistance)
                {
                    Main.LogDebug($"[v2.2.36] {unit.CharacterName}: HideAway trigger (enemy at {nearestEnemyDist:F1}m <= {minSafeDistance}m)");
                    return true;
                }

                return false;
            }

            /// <summary>
            /// ★ v2.2.50: 모든 무기 슬롯/세트에서 원거리 무기가 있는지 확인
            /// </summary>
            private static bool HasAnyRangedWeapon(BaseUnitEntity unit)
            {
                try
                {
                    // 주 무기
                    var primaryHand = unit.Body?.PrimaryHand;
                    if (primaryHand?.HasWeapon == true && primaryHand.Weapon?.Blueprint != null)
                    {
                        if (!primaryHand.Weapon.Blueprint.IsMelee) return true;
                    }

                    // 보조 무기
                    var secondaryHand = unit.Body?.SecondaryHand;
                    if (secondaryHand?.HasWeapon == true && secondaryHand.Weapon?.Blueprint != null)
                    {
                        if (!secondaryHand.Weapon.Blueprint.IsMelee) return true;
                    }

                    // 다른 무기 세트
                    var handsSets = unit.Body?.HandsEquipmentSets;
                    if (handsSets != null)
                    {
                        foreach (var set in handsSets)
                        {
                            if (set?.PrimaryHand?.HasWeapon == true && set.PrimaryHand.Weapon?.Blueprint != null)
                            {
                                if (!set.PrimaryHand.Weapon.Blueprint.IsMelee) return true;
                            }
                            if (set?.SecondaryHand?.HasWeapon == true && set.SecondaryHand.Weapon?.Blueprint != null)
                            {
                                if (!set.SecondaryHand.Weapon.Blueprint.IsMelee) return true;
                            }
                        }
                    }
                }
                catch { }

                return false;
            }

            /// <summary>
            /// ★ v2.2.50: 유닛이 적의 위협 범위 내에 있는지 확인
            /// 위협 범위 내에서는 원거리 공격 시 CannotUseInThreatenedArea 발생
            /// </summary>
            private static bool IsInThreatenedArea(BaseUnitEntity unit)
            {
                try
                {
                    var combatState = unit.CombatState;
                    if (combatState == null) return false;

                    // ★ v2.2.50: 게임 API 사용 - IsEngaged
                    if (combatState.IsEngaged)
                    {
                        return true;
                    }

                    // 폴백: 가까운 적 중 근접 공격 가능한 적이 있는지 확인
                    var allUnits = Kingmaker.Game.Instance?.State?.AllBaseAwakeUnits;
                    if (allUnits != null)
                    {
                        foreach (var other in allUnits)
                        {
                            if (other == null || other == unit) continue;
                            if (other.LifeState.IsDead) continue;
                            if (!other.IsPlayerEnemy) continue;

                            // 근접 범위 확인 (약 1.5m ~ 3m)
                            float dist = UnityEngine.Vector3.Distance(unit.Position, other.Position);
                            if (dist <= 3f)
                            {
                                // 적이 근접 무기를 가지고 있으면 위협 범위
                                var enemyWeapon = other.GetFirstWeapon();
                                if (enemyWeapon?.Blueprint?.IsMelee == true)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
                catch { }

                return false;
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
                    // ★ v2.2.45: PreferRanged 유닛의 근접 공격 차단
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

                            // ★ v2.2.45: PreferRanged 유닛이 근접 공격하려고 하면 차단!
                            if (ShouldBlockMeleeForRanged(context, unit, settings))
                            {
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
            /// ★ v2.2.45: PreferRanged 유닛이 근접 공격을 시도하는지 확인
            /// 원거리 선호 유닛이 근접 무기(지팡이 등)로 공격하려고 하면 차단
            /// </summary>
            private static bool ShouldBlockMeleeForRanged(DecisionContext context, BaseUnitEntity caster, CharacterSettings settings)
            {
                var ability = context.Ability;
                if (ability == null) return false;

                // PreferRanged 또는 MaintainRange가 아니면 체크 안함
                var pref = settings.RangePreference;
                if (pref != RangePreference.PreferRanged && pref != RangePreference.MaintainRange)
                    return false;

                // 근접 공격인지 확인 (AbilityData.IsMelee 또는 블루프린트 이름으로)
                bool isMelee = ability.IsMelee;

                // 블루프린트 이름으로 추가 확인
                string bpName = ability.Blueprint?.name?.ToLowerInvariant() ?? "";
                if (!isMelee)
                {
                    isMelee = bpName.Contains("melee") || bpName.Contains("swordsmelee");
                }

                if (!isMelee) return false;

                // 원거리 공격 옵션이 있는지 확인
                bool hasRangedOption = HasAvailableRangedAttack(caster, context.AbilityTarget);

                if (hasRangedOption)
                {
                    Main.Log($"[v2.2.45] {caster.CharacterName}: BLOCKED melee attack ({ability.Name}) - PreferRanged with ranged options available");
                    return true;
                }
                else
                {
                    // 원거리 옵션이 없으면 근접 허용 (폴백)
                    Main.Log($"[v2.2.45] {caster.CharacterName}: Allowed melee attack ({ability.Name}) - no ranged options available (fallback)");
                    return false;
                }
            }

            /// <summary>
            /// ★ v2.2.45: 유닛이 현재 타겟에게 사용할 수 있는 원거리 공격이 있는지 확인
            /// </summary>
            private static bool HasAvailableRangedAttack(BaseUnitEntity unit, TargetWrapper target)
            {
                var abilities = unit?.Abilities?.RawFacts;
                if (abilities == null) return false;

                foreach (var ability in abilities)
                {
                    var abilityData = ability?.Data;
                    if (abilityData == null) continue;

                    // 근접 공격 제외
                    if (GameAPI.IsMeleeAbility(abilityData)) continue;

                    // 무기 공격이거나 공격 스킬인지 확인
                    bool isWeaponAttack = abilityData.Weapon != null;
                    bool isAttackSkill = abilityData.Blueprint?.name?.ToLowerInvariant()?.Contains("attack") == true;
                    if (!isWeaponAttack && !isAttackSkill)
                        continue;

                    // 사용 가능한지 확인 (AP, 쿨다운 등)
                    List<string> reasons;
                    if (!GameAPI.IsAbilityAvailable(abilityData, out reasons)) continue;

                    // 타겟이 있으면 사거리 확인
                    if (target?.Entity is BaseUnitEntity targetUnit)
                    {
                        float distance = (unit.Position - targetUnit.Position).magnitude;
                        int range = GameAPI.GetAbilityRange(abilityData);
                        if (distance > range + 1f) continue;  // 사거리 밖
                    }

                    // 사용 가능한 원거리 공격 발견
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
        /// ★ v2.2.62 수정: _selectedUnitId도 유지!
        ///
        /// 이전 문제 (v2.2.61):
        /// - ClearTracking()이 _selectedUnitId를 지움
        /// - 다음 SelectAbilityTarget에서 isNewDecision=True
        /// - _lastDecisionKey가 초기화되어 반복 감지 불가
        /// - 같은 결정을 계속 반복하다 턴 종료
        ///
        /// 해결: _selectedUnitId는 ClearTracking에서 지우지 않음
        /// → 유닛이 바뀔 때만 isNewDecision=True가 됨
        /// → 같은 유닛의 반복 결정 감지 가능
        /// </summary>
        public static void ClearTracking()
        {
            _selectedAbility = null;
            _selectedTarget = null;
            // ★ v2.2.62: _selectedUnitId는 지우지 않음! 반복 감지에 필요
            // _selectedUnitId = null;  // 제거됨!
            // _consecutiveFailures - 유지
            // _lastDecisionKey - 유지
            // _lastDecisionFrame - 유지
        }

        /// <summary>
        /// ★ v2.2.17: 턴 시작 시 블랙리스트 초기화
        /// ★ v2.2.55: 액션 카운터도 리셋
        /// ★ v2.2.62: _selectedUnitId도 여기서 명시적 초기화
        /// </summary>
        public static void OnTurnStart()
        {
            _failedTargets.Clear();
            _consecutiveFailures = 0;
            _lastDecisionKey = null;
            _lastDecisionFrame = 0;
            _actionCounter = 0;
            _actionCounterUnitId = null;
            ClearTracking();
            _selectedUnitId = null;  // ★ v2.2.62: 턴 시작 시만 초기화
        }

        /// <summary>
        /// ★ v2.2.17: 전투 시작/종료 시 모든 상태 초기화
        /// ★ v2.2.55: 액션 카운터도 리셋
        /// ★ v2.2.62: _selectedUnitId도 여기서 명시적 초기화
        /// </summary>
        public static void OnCombatStateChanged()
        {
            _failedTargets.Clear();
            _consecutiveFailures = 0;
            _lastDecisionKey = null;
            _lastDecisionFrame = 0;
            _actionCounter = 0;
            _actionCounterUnitId = null;
            ClearTracking();
            _selectedUnitId = null;  // ★ v2.2.62: 전투 상태 변경 시만 초기화
            Main.LogDebug("[v2.2.17] Combat state changed - all tracking reset");
        }

        #endregion

        #region ★ v2.2.23: TaskNodeSetupMoveCommand 패치 - ToClosestEnemy 차단!

        /// <summary>
        /// ★ v2.2.23: 핵심 패치!
        /// 원거리 플레이어 컴패니언이 ToClosestEnemy로 적에게 돌진하는 것을 방지
        ///
        /// ★ v2.2.35: 조건부 차단으로 변경!
        /// - 후퇴가 필요한 경우에만 ToClosestEnemy 차단
        /// - 적이 멀리 있으면 ToClosestEnemy 허용 (이동해서 공격 가능)
        ///
        /// 문제: MovementDecisionSubtree에서 IsMovementInfluentAbility 조건이 실패하면
        ///       LoopOverAbilities → ToClosestEnemy 폴백으로 적에게 달려감!
        ///
        /// 해결: TaskNodeSetupMoveCommand.TickInternal에서 ClosestEnemy 모드일 때
        ///       후퇴가 필요한 원거리 컴패니언만 차단 (Failure 반환)
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

                    // ★ v2.2.35: 후퇴 조건 확인 - RangePreference + 적 거리
                    if (ShouldBlockToClosestEnemy(unit, settings))
                    {
                        // ★ 후퇴가 필요한 원거리 캐릭터 → ToClosestEnemy 차단!
                        Main.Log($"[v2.2.35] {unit.CharacterName}: BLOCKED ToClosestEnemy (retreat needed) - will attack from current position");
                        __result = Status.Failure;
                        return false;  // 원본 실행 안함
                    }

                    // ★ v2.2.35: 후퇴 불필요 시 ToClosestEnemy 허용
                    // 원거리 캐릭터도 적이 멀면 이동해서 공격 가능
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[Patch] SetupMoveCommand error: {ex.Message}");
                }

                return true;  // 원본 실행
            }

            /// <summary>
            /// ★ v2.2.35: ToClosestEnemy 차단 여부 결정
            /// ★ v2.2.53: PreferRanged 유닛은 항상 ToClosestEnemy 차단!
            ///
            /// 이전 문제: 적이 안전거리 밖이면 ToClosestEnemy 허용
            /// → Pascal이 적을 향해 전진 → 이상한 움직임
            ///
            /// v2.2.53 수정: PreferRanged는 절대 적에게 접근 안함
            /// → FindBetterPlace로 최적 원거리 위치 결정
            /// </summary>
            private static bool ShouldBlockToClosestEnemy(BaseUnitEntity unit, Settings.CharacterSettings settings)
            {
                // 1. RangePreference 확인 - 원거리 선호가 아니면 차단 안함
                var rangePreference = settings?.RangePreference ?? Settings.RangePreference.Adaptive;
                if (rangePreference != Settings.RangePreference.PreferRanged &&
                    rangePreference != Settings.RangePreference.MaintainRange)
                {
                    return false;  // 원거리 선호 아님 → ToClosestEnemy 허용
                }

                // 2. 원거리 무기 확인
                if (!HasRangedWeapon(unit))
                {
                    return false;  // 원거리 무기 없음 → ToClosestEnemy 허용
                }

                // ★ v2.2.53: PreferRanged 유닛은 항상 ToClosestEnemy 차단!
                // 적에게 접근하는 대신 FindBetterPlace가 최적 위치 결정
                Main.Log($"[v2.2.53] {unit.CharacterName}: BLOCKED ToClosestEnemy (PreferRanged - never approach enemies)");
                return true;
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

        #region ★ v2.2.43: TaskNodeFindBetterPlace 패치 - 원거리 캐릭터 위치 결정!

        /// <summary>
        /// ★ v2.2.43: 핵심 패치!
        /// 게임의 AttackEffectivenessTileScorer가 가까운 위치를 선호하는 문제 해결
        ///
        /// 문제: ClosinessScore = (maxRange - distance) / maxRange
        ///       → 가까울수록 점수가 높음 → 원거리 캐릭터가 적에게 너무 가까이 감!
        ///
        /// 해결: 원거리 선호 캐릭터는 MovementAPI로 직접 위치 결정
        ///       → 안전거리 유지 + 사거리 내 + LOS 확보
        /// </summary>
        [HarmonyPatch(typeof(TaskNodeFindBetterPlace))]
        public static class FindBetterPlacePatch
        {
            [HarmonyPatch("TickInternal")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.High)]
            static bool Prefix(TaskNodeFindBetterPlace __instance, Blackboard blackboard, ref Status __result)
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

                    // ★ v2.2.43: 원거리 선호 설정이 아니면 게임 AI 사용
                    var rangePreference = settings.RangePreference;
                    if (rangePreference != RangePreference.PreferRanged &&
                        rangePreference != RangePreference.MaintainRange)
                    {
                        return true;  // 게임 AI에 위임
                    }

                    // ★ v2.2.47: 원거리 무기 OR 사이킥 능력 확인
                    // 사이커는 물리적 원거리 무기 없이도 원거리 공격 가능!
                    if (!HasRangedWeapon(unit) && !HasRangedAbility(unit))
                    {
                        Main.LogDebug($"[v2.2.47] {unit.CharacterName}: No ranged weapon or ability - using game AI");
                        return true;  // 원거리 수단 없으면 게임 AI에 위임
                    }

                    Main.Log($"[v2.2.47] {unit.CharacterName}: Using custom ranged positioning");

                    // ★ v2.2.43: MovementAPI로 최적 위치 찾기
                    __result = FindBetterPlaceForRanged(context, unit, settings);
                    return false;  // 원본 메서드 실행 안함
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[Patch] FindBetterPlace error: {ex.Message}");
                    return true;  // 오류 시 원본 실행
                }
            }

            /// <summary>
            /// ★ v2.2.43: 원거리 캐릭터용 최적 위치 찾기
            /// </summary>
            private static Status FindBetterPlaceForRanged(DecisionContext context, BaseUnitEntity unit, CharacterSettings settings)
            {
                context.IsMoveCommand = true;

                // 적 목록 가져오기
                var enemies = GameAPI.GetEnemies(unit);
                if (enemies == null || enemies.Count == 0)
                {
                    Main.LogDebug($"[v2.2.43] {unit.CharacterName}: No enemies found");
                    context.IsMoveCommand = false;
                    return Status.Failure;
                }

                // ★ v2.2.48: 사거리 결정
                // 게임 AI 분석 결과 (AbilityInfo.cs):
                // - effectiveRange = ability.Weapon?.AttackOptimalRange ?? ability.RangeCells
                // - Unlimited(100000) 능력은 range 기반 위치 계산이 무의미 (ClosinessScore ≈ 동일)
                // → Cover/SafeDistance가 결정 요소
                float weaponRange = 15f;  // 기본값
                bool isUnlimitedRange = false;

                if (context.Ability != null)
                {
                    int abilityRange = GameAPI.GetAbilityRange(context.Ability);
                    if (abilityRange >= 10000)
                    {
                        isUnlimitedRange = true;
                        Main.LogDebug($"[v2.2.48] {unit.CharacterName}: Unlimited range ability detected");
                    }
                    else if (abilityRange > 0)
                    {
                        weaponRange = abilityRange;
                    }
                }

                // Unlimited이거나 능력 없으면 원거리 무기에서 찾기
                if (isUnlimitedRange || context.Ability == null)
                {
                    try
                    {
                        var primaryHand = unit.Body?.PrimaryHand;
                        if (primaryHand?.HasWeapon == true && !primaryHand.Weapon.Blueprint.IsMelee)
                        {
                            // 게임 AI와 동일: AttackOptimalRange 우선
                            int optRange = primaryHand.Weapon.AttackOptimalRange;
                            if (optRange > 0 && optRange < 10000)
                            {
                                weaponRange = optRange;
                                isUnlimitedRange = false;
                                Main.LogDebug($"[v2.2.48] {unit.CharacterName}: Using weapon optimal range {weaponRange}");
                            }
                            else
                            {
                                int attackRange = primaryHand.Weapon.AttackRange;
                                if (attackRange > 0 && attackRange < 10000)
                                {
                                    weaponRange = attackRange;
                                    isUnlimitedRange = false;
                                    Main.LogDebug($"[v2.2.48] {unit.CharacterName}: Using weapon attack range {weaponRange}");
                                }
                            }
                        }
                        else
                        {
                            var rangedAttack = GameAPI.FindAnyAttackAbility(unit, Settings.RangePreference.PreferRanged);
                            if (rangedAttack != null && !GameAPI.IsUnlimitedRange(rangedAttack))
                            {
                                weaponRange = GameAPI.GetAbilityRange(rangedAttack);
                                isUnlimitedRange = false;
                                Main.LogDebug($"[v2.2.48] {unit.CharacterName}: Using ranged ability range {weaponRange} from {rangedAttack.Name}");
                            }
                        }
                    }
                    catch { }
                }

                float minSafeDistance = settings?.MinSafeDistance ?? 5f;

                // ★ v2.2.48: Unlimited 능력은 Cover+SafeDistance 우선, range는 맵 크기로 설정
                if (isUnlimitedRange)
                {
                    weaponRange = 50f;  // 맵 대부분 커버 - 사거리 체크 무력화
                    Main.Log($"[v2.2.48] {unit.CharacterName}: Unlimited range - Cover+SafeDistance priority (effective range={weaponRange})");
                }
                else
                {
                    Main.Log($"[v2.2.48] {unit.CharacterName}: Finding ranged position (range={weaponRange}, safeDist={minSafeDistance})");
                }

                // ★ MovementAPI로 최적 위치 찾기
                var bestPosition = MovementAPI.FindRangedAttackPositionSync(unit, enemies, weaponRange, minSafeDistance);

                if (bestPosition == null || bestPosition.Node == null)
                {
                    Main.Log($"[v2.2.43] {unit.CharacterName}: No suitable ranged position found - using current position");

                    // 현재 위치에서 버티기
                    var currentNode = unit.Position.GetNearestNodeXZ() as CustomGridNodeBase;
                    if (currentNode != null && context.UnitMoveVariants.cells != null &&
                        context.UnitMoveVariants.cells.TryGetValue(currentNode, out var currentCell))
                    {
                        context.FoundBetterPlace = new DecisionContext.BetterPlace
                        {
                            PathData = context.UnitMoveVariants,
                            BestCell = currentCell
                        };
                        context.IsMoveCommand = false;
                        return Status.Success;
                    }

                    context.IsMoveCommand = false;
                    return Status.Failure;
                }

                // ★ v2.2.43: 최적 위치로 BestCell 설정
                // UnitMoveVariants에서 해당 노드의 cell 정보 가져오기
                if (context.UnitMoveVariants.cells != null &&
                    context.UnitMoveVariants.cells.TryGetValue(bestPosition.Node, out var bestCell))
                {
                    // AP 비용 확인 - 이동 가능한지 체크
                    float availableAP = unit.CombatState?.ActionPointsBlue ?? 0f;

                    if (bestCell.Length > availableAP)
                    {
                        // AP가 부족하면 경로상에서 갈 수 있는 가장 먼 위치 찾기
                        Main.Log($"[v2.2.43] {unit.CharacterName}: Not enough AP ({bestCell.Length:F1} > {availableAP:F1}) - trimming path");

                        var trimmedCell = bestCell;
                        while (trimmedCell.Length > availableAP && trimmedCell.ParentNode != null)
                        {
                            if (context.UnitMoveVariants.cells.TryGetValue(trimmedCell.ParentNode, out var parentCell))
                            {
                                trimmedCell = parentCell;
                            }
                            else
                            {
                                break;
                            }
                        }
                        bestCell = trimmedCell;
                    }

                    context.FoundBetterPlace = new DecisionContext.BetterPlace
                    {
                        PathData = context.UnitMoveVariants,
                        BestCell = bestCell
                    };

                    // 현재 위치와 비교
                    var currentNode = unit.Position.GetNearestNodeXZ();
                    if (bestCell.Node == currentNode)
                    {
                        Main.Log($"[v2.2.43] {unit.CharacterName}: Already at optimal position");
                    }
                    else
                    {
                        Main.Log($"[v2.2.43] ★ {unit.CharacterName}: Moving to ranged position ({bestPosition.Position.x:F1},{bestPosition.Position.z:F1}) - {bestPosition}");
                    }

                    context.IsMoveCommand = false;
                    return Status.Success;
                }

                // UnitMoveVariants에 해당 노드가 없으면 직접 생성 시도
                Main.Log($"[v2.2.43] {unit.CharacterName}: Node not in UnitMoveVariants - falling back to game AI");
                context.IsMoveCommand = false;
                return Status.Failure;  // 게임 AI가 처리하도록
            }

            /// <summary>
            /// 원거리 무기를 가지고 있는지 확인 (중복 방지용)
            /// </summary>
            private static bool HasRangedWeapon(BaseUnitEntity unit)
            {
                try
                {
                    var primaryHand = unit.Body?.PrimaryHand;
                    if (primaryHand?.HasWeapon == true)
                    {
                        var weapon = primaryHand.Weapon;
                        if (weapon?.Blueprint != null && !weapon.Blueprint.IsMelee)
                            return true;
                    }

                    var secondaryHand = unit.Body?.SecondaryHand;
                    if (secondaryHand?.HasWeapon == true)
                    {
                        var weapon = secondaryHand.Weapon;
                        if (weapon?.Blueprint != null && !weapon.Blueprint.IsMelee)
                            return true;
                    }
                }
                catch { }

                return false;
            }

            /// <summary>
            /// ★ v2.2.47: 원거리 능력(사이킥 등)이 있는지 확인
            /// 사이커는 물리적 원거리 무기 없이도 원거리 공격 가능!
            /// </summary>
            private static bool HasRangedAbility(BaseUnitEntity unit)
            {
                try
                {
                    var abilities = unit?.Abilities?.RawFacts;
                    if (abilities == null) return false;

                    foreach (var ability in abilities)
                    {
                        var abilityData = ability?.Data;
                        if (abilityData == null) continue;

                        // 근접 능력 제외
                        if (abilityData.IsMelee) continue;

                        // 무기 공격 제외 (HasRangedWeapon에서 체크)
                        if (abilityData.Weapon != null) continue;

                        // 사거리가 1 이상인 능력 = 원거리 능력
                        int range = GameAPI.GetAbilityRange(abilityData);
                        if (range > 1)
                        {
                            // 공격/데미지 스킬인지 확인 (버프/힐 제외)
                            if (GameAPI.IsOffensiveAbility(abilityData))
                            {
                                return true;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[HasRangedAbility] Error: {ex.Message}");
                }

                return false;
            }
        }

        #endregion

        #region ★ v2.2.46: IsUsualMeleeUnit 패치 - 핵심 수정!

        /// <summary>
        /// ★ v2.2.46: 근본적인 해결책!
        ///
        /// 문제: 게임의 MovementDecisionSubtree가 IsUsualMeleeUnit 플래그로 이동 방식 결정
        ///       - IsUsualMeleeUnit == true  → ToClosestEnemy (적에게 돌진!)
        ///       - IsUsualMeleeUnit == false → IsMovementInfluentAbility 체크 → FindBetterPlace
        ///
        /// 카시아의 워프 가이드 스태프가 IsMelee=true라서 IsUsualMeleeUnit=true가 되어
        /// ToClosestEnemy로 직행해버림!
        ///
        /// 해결: PreferRanged/MaintainRange 설정된 유닛은 IsUsualMeleeUnit = false 반환
        ///       → FindBetterPlace 경로로 가서 우리 패치가 적용됨!
        /// </summary>
        [HarmonyPatch(typeof(PartUnitBrain))]
        public static class IsUsualMeleeUnitPatch
        {
            // ★ v2.2.50: 로그 스팸 방지 - 유닛당 한 번만 로그
            private static HashSet<string> _loggedUnits = new HashSet<string>();

            [HarmonyPatch("IsUsualMeleeUnit", MethodType.Getter)]
            [HarmonyPostfix]
            static void Postfix(PartUnitBrain __instance, ref bool __result)
            {
                try
                {
                    // 이미 false면 수정 필요 없음
                    if (!__result) return;

                    var unit = __instance?.Owner as BaseUnitEntity;
                    if (unit == null || !unit.IsDirectlyControllable) return;

                    // 설정 확인
                    var settings = Main.Settings?.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
                    if (settings == null || !settings.EnableCustomAI) return;

                    // ★ v2.2.46: PreferRanged/MaintainRange는 IsUsualMeleeUnit = false!
                    var rangePreference = settings.RangePreference;
                    if (rangePreference == RangePreference.PreferRanged ||
                        rangePreference == RangePreference.MaintainRange)
                    {
                        __result = false;  // 근접 유닛이 아님 → FindBetterPlace 경로!

                        // ★ v2.2.50: 로그 스팸 방지 - 턴당 한 번만
                        if (!_loggedUnits.Contains(unit.UniqueId))
                        {
                            Main.LogDebug($"[v2.2.46] {unit.CharacterName}: IsUsualMeleeUnit overridden to FALSE (PreferRanged)");
                            _loggedUnits.Add(unit.UniqueId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[Patch] IsUsualMeleeUnit error: {ex.Message}");
                }
            }

            /// <summary>
            /// ★ v2.2.50: 새 라운드 시작 시 로그 캐시 초기화
            /// </summary>
            public static void ClearLogCache()
            {
                _loggedUnits.Clear();
            }
        }

        #endregion
    }
}
