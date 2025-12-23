using System;
using HarmonyLib;
using Kingmaker.AI;
using Kingmaker.AI.BehaviourTrees;
using Kingmaker.AI.BehaviourTrees.Nodes;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Utility;
using CompanionAI_v2_2.Core;
using CompanionAI_v2_2.Settings;

namespace CompanionAI_v2_2.Patches
{
    /// <summary>
    /// v2.2.0: 커스텀 AI 패치 - 타이밍 인식 시스템
    ///
    /// TaskNodeSelectAbilityTarget.TickInternal을 가로채서
    /// 커스텀 AI 로직으로 대체
    /// </summary>
    public static class CustomAIPatch
    {
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

                    // v2.2 로직 실행
                    __result = ExecuteCustomLogic(context, unit, settings);
                    return false;  // 원본 메서드 실행 안함
                }
                catch (Exception ex)
                {
                    Main.LogError($"[Patch] SelectAbilityTarget error: {ex}");
                    return true;  // 오류 시 원본 실행
                }
            }

            private static Status ExecuteCustomLogic(
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

                    Main.Log($"[v2.2] DECISION: {unit.CharacterName} uses {ability.Name} -> {GetTargetName(target)}");
                    return Status.Success;
                }

                // 능력 선택 실패 - 게임 기본 AI가 이동 등 다른 행동 결정
                Main.LogDebug($"[v2.2] {unit.CharacterName}: No ability selected, delegating to game AI");
                return Status.Failure;
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
    }
}
