using System.Collections.Generic;
using System.Linq;
using CompanionAI_v2.Core;
using CompanionAI_v2.Settings;

namespace CompanionAI_v2.Strategies
{
    /// <summary>
    /// v2.1.0: Strategy Factory - 역할에 따른 전략 생성
    /// </summary>
    public static class StrategyFactory
    {
        private static readonly Dictionary<UnitRole, IUnitStrategy> _strategies = new Dictionary<UnitRole, IUnitStrategy>
        {
            { UnitRole.Tank, new TankStrategy() },
            { UnitRole.DPS, new DPSStrategy() },
            { UnitRole.Support, new SupportStrategy() },
            { UnitRole.Balanced, new BalancedStrategy() },
            { UnitRole.Sniper, new SniperStrategy() },
            { UnitRole.Hybrid, new HybridStrategy() }
        };

        /// <summary>
        /// 역할에 맞는 전략 반환
        /// </summary>
        public static IUnitStrategy GetStrategy(UnitRole role)
        {
            if (_strategies.TryGetValue(role, out var strategy))
            {
                return strategy;
            }

            // 기본값: Balanced
            return _strategies[UnitRole.Balanced];
        }

        /// <summary>
        /// 설정에서 역할 가져오기
        /// </summary>
        public static UnitRole GetRoleFromSettings(CharacterSettings settings)
        {
            if (settings == null) return UnitRole.Balanced;

            switch (settings.Role)
            {
                case AIRole.Tank:
                    return UnitRole.Tank;
                case AIRole.DPS:
                    return UnitRole.DPS;
                case AIRole.Support:
                    return UnitRole.Support;
                case AIRole.Sniper:
                    return UnitRole.Sniper;
                case AIRole.Hybrid:
                    return UnitRole.Hybrid;
                case AIRole.Balanced:
                default:
                    return UnitRole.Balanced;
            }
        }
    }

    public enum UnitRole
    {
        Balanced,
        Tank,
        DPS,
        Support,
        Sniper,
        Hybrid
    }

    /// <summary>
    /// Sniper 전략 - 원거리 특화
    /// </summary>
    public class SniperStrategy : IUnitStrategy
    {
        public string StrategyName => "Sniper";

        public ActionDecision DecideAction(ActionContext ctx)
        {
            Main.Log($"[Sniper] {ctx.Unit.CharacterName}: Distance to nearest={ctx.NearestEnemyDistance:F1}");

            // 1. 근접 범위면 후퇴
            if (ctx.IsInMeleeRange && ctx.CanMove)
            {
                return ActionDecision.Move("Retreating from melee");
            }

            // 2. 가장 약한 적 원거리 공격
            var target = ctx.WeakestEnemy ?? ctx.NearestEnemy;
            if (target == null)
            {
                return ActionDecision.EndTurn("No target");
            }

            var targetWrapper = new Kingmaker.Utility.TargetWrapper(target);

            // 원거리 능력만 사용
            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsRangedAbility(ability)) continue;

                if (GameAPI.IsDangerousAoE(ability))
                {
                    int alliesInPattern = GameAPI.CountAlliesInPattern(ability, targetWrapper, ctx.Allies);
                    if (alliesInPattern > 0) continue;
                }

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[Sniper] Shot: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Snipe {target.CharacterName}");
                }
            }

            // 이동해서 사거리 확보
            if (ctx.CanMove)
            {
                return ActionDecision.Move("Repositioning for shot");
            }

            return ActionDecision.EndTurn("No valid shot");
        }
    }

    /// <summary>
    /// Hybrid 전략 - 상황에 따라 근접/원거리 전환
    /// </summary>
    public class HybridStrategy : IUnitStrategy
    {
        public string StrategyName => "Hybrid";

        public ActionDecision DecideAction(ActionContext ctx)
        {
            Main.Log($"[Hybrid] {ctx.Unit.CharacterName}: InMelee={ctx.IsInMeleeRange}, HasMelee={ctx.HasMeleeWeapon}, HasRanged={ctx.HasRangedWeapon}");

            // 상황에 따라 결정
            bool useMelee = ctx.IsInMeleeRange || (ctx.EnemiesInMeleeRange >= 2);

            var target = ctx.NearestEnemy;
            if (target == null)
            {
                return ActionDecision.EndTurn("No target");
            }

            var targetWrapper = new Kingmaker.Utility.TargetWrapper(target);

            // 근접/원거리 선택
            var abilities = ctx.AvailableAbilities;
            if (useMelee)
            {
                abilities = abilities.OrderByDescending(a => GameAPI.IsMeleeAbility(a) ? 1 : 0).ToList();
            }
            else
            {
                abilities = abilities.OrderByDescending(a => GameAPI.IsRangedAbility(a) ? 1 : 0).ToList();
            }

            foreach (var ability in abilities)
            {
                if (!GameAPI.IsOffensiveAbility(ability) && !GameAPI.IsMeleeAbility(ability) && !GameAPI.IsRangedAbility(ability))
                    continue;

                if (GameAPI.IsDangerousAoE(ability))
                {
                    int alliesInPattern = GameAPI.CountAlliesInPattern(ability, targetWrapper, ctx.Allies);
                    if (alliesInPattern > 0) continue;
                }

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[Hybrid] Attack: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Hybrid attack");
                }
            }

            // 이동
            if (ctx.CanMove)
            {
                return ActionDecision.Move(useMelee ? "Closing to melee" : "Repositioning");
            }

            return ActionDecision.EndTurn("No valid action");
        }
    }
}
