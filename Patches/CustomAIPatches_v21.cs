using System;
using HarmonyLib;
using Kingmaker.AI;
using Kingmaker.AI.BehaviourTrees;
using Kingmaker.AI.BehaviourTrees.Nodes;
using Kingmaker.EntitySystem.Entities;
using CompanionAI_v2.Core;
using CompanionAI_v2.Settings;

namespace CompanionAI_v2.Patches
{
    /// <summary>
    /// v2.1.0: 새로운 전략 기반 AI 패치
    /// - 점수 기반 대신 우선순위 기반 결정
    /// - 게임 API 직접 활용
    /// - Strategy 패턴으로 역할 분리
    /// </summary>
    public static class CustomAIPatches_v21
    {
        /// <summary>
        /// v2.1 사용 여부 (false면 기존 CustomAIPatches 사용)
        /// </summary>
        public static bool UseV21 { get; set; } = true;

        #region Main Patch - TaskNodeSelectAbilityTarget

        [HarmonyPatch(typeof(TaskNodeSelectAbilityTarget))]
        public static class SelectAbilityTargetPatch
        {
            [HarmonyPatch("TickInternal")]
            [HarmonyPrefix]
            [HarmonyPriority(Priority.High)]  // 기존 패치보다 먼저 실행
            static bool Prefix(TaskNodeSelectAbilityTarget __instance, Blackboard blackboard, ref Status __result)
            {
                // v2.1 비활성화면 기존 로직으로 패스
                if (!UseV21) return true;

                try
                {
                    var context = blackboard?.DecisionContext;
                    if (context == null) return true;

                    var unit = context.Unit;
                    if (unit == null || !unit.IsDirectlyControllable) return true;

                    // 설정 확인
                    var settings = Main.Settings.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
                    if (!settings.EnableCustomAI) return true;

                    // v2.1 Orchestrator 호출
                    __result = ExecuteV21Logic(context, unit, settings);
                    return false;  // 원본 메서드 실행 안함
                }
                catch (Exception ex)
                {
                    Main.LogError($"[v2.1] SelectAbilityTarget Prefix error: {ex}");
                    return true;  // 오류 시 원본 실행
                }
            }

            /// <summary>
            /// v2.1 로직 실행
            /// </summary>
            private static Status ExecuteV21Logic(
                DecisionContext context,
                BaseUnitEntity unit,
                CharacterSettings settings)
            {
                // 초기화
                context.AbilityTarget = null;
                context.Ability = null;

                // Orchestrator 호출
                var (ability, target) = AIOrchestrator.DecideAction(context, unit, settings);

                if (ability != null && target != null)
                {
                    context.Ability = ability;
                    context.AbilityTarget = target;

                    Main.Log($"[v2.1] DECISION: {unit.CharacterName} uses {ability.Name} -> {GetTargetName(target)}");
                    return Status.Success;
                }

                // 능력 선택 실패 - 게임 기본 AI가 이동 등 다른 행동 결정
                Main.LogDebug($"[v2.1] {unit.CharacterName}: No ability selected, delegating to game AI");
                return Status.Failure;
            }

            private static string GetTargetName(Kingmaker.Utility.TargetWrapper target)
            {
                if (target == null) return "null";
                if (target.Entity is BaseUnitEntity unit)
                    return unit.CharacterName;
                if (target.Point != UnityEngine.Vector3.zero)
                    return $"Point({target.Point.x:F1}, {target.Point.z:F1})";
                return "Unknown";
            }
        }

        #endregion

        #region Movement Patch (Optional - 전략이 Move 결정 시 사용)

        // 이동 관련 패치는 기존 게임 AI에 위임
        // 전략이 ActionType.Move를 반환하면 Status.Failure를 반환하여
        // 게임의 이동 로직이 실행되도록 함

        #endregion
    }
}
