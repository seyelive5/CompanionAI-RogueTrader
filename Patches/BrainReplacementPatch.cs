using System;
using System.Reflection;
using HarmonyLib;
using Kingmaker.Controllers.TurnBased;
using Kingmaker.EntitySystem.Entities;
using CompanionAI_v2_2.Core;

namespace CompanionAI_v2_2.Patches
{
    /// <summary>
    /// ★ 핵심 패치: TurnController 패치
    /// - IsAiTurn을 true로, IsPlayerTurn을 false로 만들어 게임 AI가 동작하게 함
    /// - 이 패치 없이는 플레이어 캐릭터에 대해 AI가 실행되지 않음!
    /// </summary>
    [HarmonyPatch(typeof(TurnController))]
    public static class TurnControllerPatches
    {
        /// <summary>
        /// IsPlayerTurn 패치 - AI 활성화 시 false 반환
        /// 플레이어가 수동으로 조작하지 못하게 함
        /// </summary>
        [HarmonyPatch(nameof(TurnController.IsPlayerTurn), MethodType.Getter)]
        [HarmonyPostfix]
        private static void IsPlayerTurn_Postfix(TurnController __instance, ref bool __result)
        {
            try
            {
                if (!Main.Enabled) return;

                // 전투 상태 변화 감지 (전투 시작/종료 시 상태 초기화)
                CombatStateListener.CheckCombatState(__instance);

                var currentUnit = __instance.CurrentUnit as BaseUnitEntity;
                if (currentUnit == null) return;

                // 플레이어 파티가 아니면 패스
                if (!currentUnit.IsInPlayerParty) return;

                var settings = Main.Settings?.GetOrCreateSettings(currentUnit.UniqueId, currentUnit.CharacterName);
                if (settings == null) return;

                if (settings.EnableCustomAI)
                {
                    // AI 활성화 시 플레이어 턴이 아님
                    __result = false;
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[Patch] IsPlayerTurn error: {ex}");
            }
        }

        /// <summary>
        /// IsAiTurn 패치 - AI 활성화 시 true 반환
        /// AiBrainController.Tick()가 실행되도록 함
        /// </summary>
        [HarmonyPatch(nameof(TurnController.IsAiTurn), MethodType.Getter)]
        [HarmonyPostfix]
        private static void IsAiTurn_Postfix(TurnController __instance, ref bool __result)
        {
            try
            {
                if (!Main.Enabled) return;

                var currentUnit = __instance.CurrentUnit as BaseUnitEntity;
                if (currentUnit == null) return;

                // 플레이어 파티가 아니면 패스
                if (!currentUnit.IsInPlayerParty) return;

                var settings = Main.Settings?.GetOrCreateSettings(currentUnit.UniqueId, currentUnit.CharacterName);
                if (settings == null) return;

                if (settings.EnableCustomAI)
                {
                    // AI 활성화 시 AI 턴임
                    __result = true;
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[Patch] IsAiTurn error: {ex}");
            }
        }
    }

    /// <summary>
    /// IsAIEnabled 패치 - 게임이 AI 활성화 상태를 인식하게 함
    /// </summary>
    [HarmonyPatch]
    public static class IsAIEnabledPatch
    {
        static MethodBase TargetMethod()
        {
            try
            {
                var assembly = typeof(BaseUnitEntity).Assembly;
                var partUnitBrainType = assembly.GetType("Kingmaker.UnitLogic.PartUnitBrain");

                if (partUnitBrainType == null)
                {
                    Main.LogError("[Patch] PartUnitBrain type not found!");
                    return null;
                }

                var property = partUnitBrainType.GetProperty("IsAIEnabled", BindingFlags.Public | BindingFlags.Instance);
                if (property == null)
                {
                    Main.LogError("[Patch] IsAIEnabled property not found!");
                    return null;
                }

                var method = property.GetGetMethod();
                if (method != null)
                {
                    Main.Log("[Patch] IsAIEnabled patch initialized successfully");
                }
                return method;
            }
            catch (Exception ex)
            {
                Main.LogError($"[Patch] Error in TargetMethod: {ex}");
                return null;
            }
        }

        static void Postfix(object __instance, ref bool __result)
        {
            try
            {
                // 이미 AI가 활성화되어 있으면 패스
                if (__result)
                    return;

                // Owner 가져오기
                var ownerProperty = __instance.GetType().GetProperty("Owner");
                if (ownerProperty == null)
                    return;

                var owner = ownerProperty.GetValue(__instance) as BaseUnitEntity;
                if (owner == null)
                    return;

                // 직접 컨트롤 가능한 유닛인지 확인 (플레이어 캐릭터/컴패니언)
                if (!owner.IsDirectlyControllable)
                    return;

                // 설정 확인
                var settings = Main.Settings?.GetOrCreateSettings(owner.UniqueId, owner.CharacterName);
                if (settings == null)
                    return;

                // Custom AI가 활성화되어 있으면 게임 AI 활성화
                if (settings.EnableCustomAI)
                {
                    __result = true;
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[Patch] Error in IsAIEnabled Postfix: {ex}");
            }
        }
    }
}
