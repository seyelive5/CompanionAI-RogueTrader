using System.Collections.Generic;
using CompanionAI_v2_2.Core;
using CompanionAI_v2_2.Settings;

namespace CompanionAI_v2_2.Strategies
{
    /// <summary>
    /// v2.2.0: 전략 팩토리 - 역할에 따른 전략 인스턴스 제공
    /// ★ v2.2.27: Hybrid/Sniper 제거
    /// </summary>
    public static class StrategyFactory
    {
        private static readonly Dictionary<AIRole, IUnitStrategy> _strategies = new();

        static StrategyFactory()
        {
            // 전략 인스턴스 초기화
            _strategies[AIRole.Balanced] = new BalancedStrategy();
            _strategies[AIRole.Tank] = new TankStrategy();
            _strategies[AIRole.DPS] = new DPSStrategy();
            _strategies[AIRole.Support] = new SupportStrategy();
        }

        /// <summary>
        /// 역할에 맞는 전략 인스턴스 반환
        /// </summary>
        public static IUnitStrategy GetStrategy(AIRole role)
        {
            if (_strategies.TryGetValue(role, out var strategy))
            {
                return strategy;
            }

            // 기본값은 Balanced
            return _strategies[AIRole.Balanced];
        }
    }
}
