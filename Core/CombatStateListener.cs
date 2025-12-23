using HarmonyLib;
using Kingmaker.Controllers.TurnBased;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.1: 전투 상태 감시자
    ///
    /// 역할: 전투 시작/종료 이벤트를 감지하여 AI 상태 초기화
    /// - 전투 시작 시: AIOrchestrator의 모든 상태 초기화
    /// - 전투 종료 시: 상태 정리
    ///
    /// 이 클래스가 없으면 "전투당 1회" 스킬이 두 번째 전투부터 작동 안 함
    /// </summary>
    public static class CombatStateListener
    {
        private static bool _wasInCombat = false;

        /// <summary>
        /// 매 턴마다 호출되어 전투 상태 변화 감지
        /// TurnController 패치에서 호출됨
        /// </summary>
        public static void CheckCombatState(TurnController turnController)
        {
            if (turnController == null) return;

            bool isInCombat = turnController.TurnBasedModeActive;

            // 전투 시작 감지
            if (isInCombat && !_wasInCombat)
            {
                _wasInCombat = true;
                AIOrchestrator.OnCombatStart();
            }
            // 전투 종료 감지
            else if (!isInCombat && _wasInCombat)
            {
                _wasInCombat = false;
                AIOrchestrator.OnCombatEnd();
            }
        }
    }
}
