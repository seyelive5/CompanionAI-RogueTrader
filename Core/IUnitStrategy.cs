using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2_2.Settings;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.0: 전략 패턴 - 타이밍 인식 AI 로직
    /// </summary>
    public interface IUnitStrategy
    {
        /// <summary>전략 이름</summary>
        string StrategyName { get; }

        /// <summary>다음 행동 결정</summary>
        ActionDecision DecideAction(ActionContext context);
    }

    /// <summary>
    /// 행동 결정 컨텍스트 - 결정에 필요한 모든 정보
    /// </summary>
    public class ActionContext
    {
        // 자신
        public BaseUnitEntity Unit { get; set; }
        public float HPPercent { get; set; }
        public bool CanMove { get; set; }
        public bool CanAct { get; set; }

        // 적/아군 목록
        public List<BaseUnitEntity> Enemies { get; set; } = new List<BaseUnitEntity>();
        public List<BaseUnitEntity> Allies { get; set; } = new List<BaseUnitEntity>();

        // 사용 가능한 능력 목록
        public List<AbilityData> AvailableAbilities { get; set; } = new List<AbilityData>();

        // 분석된 타겟
        public BaseUnitEntity NearestEnemy { get; set; }
        public float NearestEnemyDistance { get; set; }
        public BaseUnitEntity WeakestEnemy { get; set; }
        public BaseUnitEntity MostWoundedAlly { get; set; }

        // ★ v2.2.9: 스코어링 기반 최적 타겟
        public BaseUnitEntity BestTarget { get; set; }
        public BaseUnitEntity BestMeleeTarget { get; set; }
        public BaseUnitEntity BestRangedTarget { get; set; }

        // 상황 분석
        public bool IsInMeleeRange { get; set; }
        public int EnemiesInMeleeRange { get; set; }
        public bool HasMeleeWeapon { get; set; }
        public bool HasRangedWeapon { get; set; }

        // ★ v2.2.0: 턴 내 행동 추적
        public bool HasPerformedFirstAction { get; set; } = false;
        public int ActionsPerformedThisTurn { get; set; } = 0;

        // 설정
        public CharacterSettings Settings { get; set; }
    }

    /// <summary>
    /// 행동 결정 결과
    /// </summary>
    public class ActionDecision
    {
        public ActionType Type { get; set; }
        public AbilityData Ability { get; set; }
        public TargetWrapper Target { get; set; }
        public string Reason { get; set; }

        public static ActionDecision UseAbility(AbilityData ability, TargetWrapper target, string reason)
        {
            return new ActionDecision
            {
                Type = ActionType.UseAbility,
                Ability = ability,
                Target = target,
                Reason = reason
            };
        }

        public static ActionDecision Move(string reason)
        {
            return new ActionDecision
            {
                Type = ActionType.Move,
                Reason = reason
            };
        }

        public static ActionDecision EndTurn(string reason)
        {
            return new ActionDecision
            {
                Type = ActionType.EndTurn,
                Reason = reason
            };
        }

        public static ActionDecision Skip(string reason)
        {
            return new ActionDecision
            {
                Type = ActionType.Skip,
                Reason = reason
            };
        }
    }

    public enum ActionType
    {
        UseAbility,
        Move,
        EndTurn,
        Skip
    }
}
