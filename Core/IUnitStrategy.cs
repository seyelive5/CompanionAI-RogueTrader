using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2.Settings;

namespace CompanionAI_v2.Core
{
    /// <summary>
    /// v2.1.0: 전략 패턴 - 역할별 AI 로직 분리
    /// 점수 기반 대신 우선순위 기반 결정
    /// </summary>
    public interface IUnitStrategy
    {
        /// <summary>
        /// 전략 이름 (디버깅용)
        /// </summary>
        string StrategyName { get; }

        /// <summary>
        /// 다음 행동 결정 - 우선순위 기반
        /// </summary>
        /// <returns>수행할 행동, null이면 턴 종료</returns>
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

        // 상황 분석
        public bool IsInMeleeRange { get; set; }  // 근접 범위에 적이 있는가
        public int EnemiesInMeleeRange { get; set; }  // 근접 범위 내 적 수
        public bool HasMeleeWeapon { get; set; }
        public bool HasRangedWeapon { get; set; }

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
        public string Reason { get; set; }  // 디버깅용

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
        UseAbility,  // 능력 사용
        Move,        // 이동 (게임 기본 AI에 위임)
        EndTurn,     // 턴 종료
        Skip         // 이번 결정 스킵 (다른 행동 시도)
    }
}
