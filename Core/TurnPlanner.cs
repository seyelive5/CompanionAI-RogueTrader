using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2_2.Settings;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// ★ v2.2.58: 통합 턴 플래너
    ///
    /// 기존 문제: 각 결정이 독립적 (버프, 이동, 공격이 서로를 모름)
    /// 해결: 턴 전체를 분석하고 최적의 행동 순서 결정
    ///
    /// 고려 요소:
    /// - AP 경제 (버프 후 공격 가능한가?)
    /// - 위치 (이동 필요? 후퇴 필요?)
    /// - 타겟 상태 (버프 없이 처치 가능?)
    /// - 전술적 가치 (엄폐, 위험도)
    /// </summary>
    public static class TurnPlanner
    {
        #region Data Structures

        /// <summary>
        /// 현재 상황 분석 결과
        /// </summary>
        public class TurnSituation
        {
            // 기본 정보
            public BaseUnitEntity Unit;
            public float CurrentAP;
            public float HPPercent;
            public RangePreference RangePreference;

            // 위치 분석
            public float NearestEnemyDistance;
            public bool IsInDanger;           // 근접 적 + 원거리 선호 = 위험
            public bool NeedsReposition;      // 공격 불가 위치
            public bool HasCoverNearby;

            // 타겟 분석
            public List<BaseUnitEntity> HittableEnemies;   // 현재 위치에서 공격 가능
            public List<BaseUnitEntity> AllEnemies;
            public BaseUnitEntity BestTarget;
            public bool CanKillBestTarget;    // 버프 없이 처치 가능

            // 능력 분석
            public List<AbilityData> AvailableBuffs;
            public List<AbilityData> AvailableAttacks;
            public AbilityData PrimaryAttack;
            public AbilityData BestBuff;

            // AP 계산
            public float PrimaryAttackCost;
            public float BestBuffCost;
            public int AttacksWithoutBuff;    // 버프 없이 가능한 공격 횟수
            public int AttacksWithBuff;       // 버프 후 가능한 공격 횟수
        }

        /// <summary>
        /// 턴 계획
        /// </summary>
        public class TurnPlan
        {
            public TurnPriority Priority;
            public float Score;
            public string Reason;

            // 행동 지침
            public bool ShouldBuffFirst;
            public bool ShouldMoveFirst;
            public bool ShouldRetreat;
            public bool ShouldSeekCover;

            public AbilityData RecommendedBuff;
            public AbilityData RecommendedAttack;
            public BaseUnitEntity RecommendedTarget;
        }

        /// <summary>
        /// 턴 우선순위
        /// </summary>
        public enum TurnPriority
        {
            Emergency,         // 긴급 힐/재장전
            Retreat,           // 후퇴 (위험 상황)
            SeekCover,         // 엄폐 확보
            BuffedAttack,      // 버프 → 공격
            DirectAttack,      // 즉시 공격
            MoveAndAttack,     // 이동 → 공격
            BuffMoveAttack,    // 버프 → 이동 → 공격
            Support,           // 아군 지원
            EndTurn            // 행동 종료
        }

        #endregion

        #region Main Entry Point

        /// <summary>
        /// 턴 계획 생성 - AIOrchestrator에서 호출
        /// </summary>
        public static TurnPlan CreatePlan(ActionContext ctx)
        {
            try
            {
                // 1. 상황 분석
                var situation = AnalyzeSituation(ctx);

                // 2. 계획 생성 및 평가
                var plan = EvaluateAndSelectPlan(situation, ctx);

                // 3. 로깅
                LogPlan(ctx.Unit.CharacterName, plan, situation);

                return plan;
            }
            catch (Exception ex)
            {
                Main.LogError($"[TurnPlanner] CreatePlan error: {ex.Message}");
                return new TurnPlan { Priority = TurnPriority.DirectAttack, Reason = "Error fallback" };
            }
        }

        #endregion

        #region Situation Analysis

        /// <summary>
        /// 전투 상황 분석
        /// </summary>
        private static TurnSituation AnalyzeSituation(ActionContext ctx)
        {
            var situation = new TurnSituation
            {
                Unit = ctx.Unit,
                CurrentAP = ctx.CurrentAP,
                HPPercent = ctx.HPPercent,
                RangePreference = ctx.Settings?.RangePreference ?? RangePreference.Adaptive,
                AllEnemies = ctx.Enemies,
                BestTarget = ctx.BestTarget,
                NearestEnemyDistance = ctx.NearestEnemyDistance
            };

            // 위치 분석
            AnalyzePosition(situation, ctx);

            // 능력 분석
            AnalyzeAbilities(situation, ctx);

            // AP 경제 계산
            CalculateAPEconomy(situation, ctx);

            // 타겟 분석
            AnalyzeTargets(situation, ctx);

            return situation;
        }

        /// <summary>
        /// 위치 상황 분석
        /// ★ v2.2.60: 모든 공격 능력으로 hittable 확인 (PrimaryWeaponAttack만 확인하던 버그 수정)
        /// </summary>
        private static void AnalyzePosition(TurnSituation situation, ActionContext ctx)
        {
            // 위험 상태 확인 (근접 적 + 원거리 선호)
            bool preferRanged = situation.RangePreference == RangePreference.PreferRanged ||
                               situation.RangePreference == RangePreference.MaintainRange;

            float dangerDistance = 5f;  // 5m 이내는 위험
            situation.IsInDanger = preferRanged && situation.NearestEnemyDistance <= dangerDistance;

            // ★ v2.2.60: 모든 공격 능력으로 hittable 적 확인
            // 이전 버그: PrimaryWeaponAttack만 확인 → 플라스마 과충전 같은 다른 공격이 있어도 "hittable 없음" 판정
            situation.HittableEnemies = new List<BaseUnitEntity>();

            // 공격 능력 목록 수집 (RangePreference 필터링 전)
            var attackAbilities = new List<AbilityData>();
            if (ctx.PrimaryWeaponAttack != null)
            {
                attackAbilities.Add(ctx.PrimaryWeaponAttack);
            }

            // 추가 공격 능력 수집
            foreach (var ability in ctx.AvailableAbilities)
            {
                if (ability == ctx.PrimaryWeaponAttack) continue;  // 중복 방지

                // 공격성 능력 또는 무기 공격
                if (GameAPI.IsOffensiveAbility(ability) || ability.Weapon != null)
                {
                    // RangePreference에 맞는 무기 타입만 (또는 Adaptive면 전부)
                    if (situation.RangePreference == RangePreference.Adaptive ||
                        CombatHelpers.IsPreferredWeaponType(ability, situation.RangePreference))
                    {
                        attackAbilities.Add(ability);
                    }
                }
            }

            // 각 적에 대해 어떤 공격으로든 hittable한지 확인
            foreach (var enemy in ctx.Enemies)
            {
                if (enemy == null || enemy.LifeState.IsDead) continue;

                var targetWrapper = new TargetWrapper(enemy);

                foreach (var attack in attackAbilities)
                {
                    string reason;
                    if (GameAPI.CanUseAbilityOn(attack, targetWrapper, out reason))
                    {
                        situation.HittableEnemies.Add(enemy);
                        Main.LogDebug($"[TurnPlanner] {enemy.CharacterName} hittable with {attack.Name}");
                        break;  // 하나라도 가능하면 hittable
                    }
                }
            }

            Main.LogDebug($"[TurnPlanner] Hittable check: {situation.HittableEnemies.Count}/{ctx.Enemies.Count} enemies with {attackAbilities.Count} attacks");

            // 이동 필요 여부
            situation.NeedsReposition = situation.HittableEnemies.Count == 0 && ctx.Enemies.Count > 0;

            // 엄폐 확인 (간단 버전 - 나중에 확장 가능)
            situation.HasCoverNearby = false;  // TODO: 실제 엄폐 검색
        }

        /// <summary>
        /// 사용 가능한 능력 분석
        /// </summary>
        private static void AnalyzeAbilities(TurnSituation situation, ActionContext ctx)
        {
            situation.AvailableBuffs = new List<AbilityData>();
            situation.AvailableAttacks = new List<AbilityData>();

            foreach (var ability in ctx.AvailableAbilities)
            {
                var timing = AbilityDatabase.GetTiming(ability);

                // 버프 분류
                if (timing == AbilityDatabase.AbilityTiming.PreAttackBuff ||
                    timing == AbilityDatabase.AbilityTiming.PreCombatBuff)
                {
                    // 자기 타겟 버프만
                    if (GameAPI.IsSelfTargetAbility(ability))
                    {
                        // 이미 활성화된 버프 제외
                        if (!GameAPI.HasActiveBuff(ctx.Unit, ability))
                        {
                            // Run and Gun은 첫 행동 전에 제외
                            if (!ctx.HasPerformedFirstAction && AbilityDatabase.IsRunAndGun(ability))
                                continue;

                            situation.AvailableBuffs.Add(ability);
                        }
                    }
                }
                // 공격 분류
                else if (GameAPI.IsOffensiveAbility(ability) || ability.Weapon != null)
                {
                    // RangePreference 필터링
                    if (CombatHelpers.IsPreferredWeaponType(ability, situation.RangePreference) ||
                        situation.RangePreference == RangePreference.Adaptive)
                    {
                        situation.AvailableAttacks.Add(ability);
                    }
                }
            }

            // 주 공격 및 버프 선택
            situation.PrimaryAttack = ctx.PrimaryWeaponAttack ??
                situation.AvailableAttacks.FirstOrDefault();
            situation.BestBuff = SelectBestBuff(situation.AvailableBuffs, ctx);
        }

        /// <summary>
        /// 최적 버프 선택
        /// </summary>
        private static AbilityData SelectBestBuff(List<AbilityData> buffs, ActionContext ctx)
        {
            if (buffs == null || buffs.Count == 0) return null;

            // 우선순위: 낮은 AP 비용 + 공격 버프
            return buffs
                .OrderBy(b => GameAPI.GetAbilityAPCost(b))
                .ThenByDescending(b => IsAttackBuff(b) ? 1 : 0)
                .FirstOrDefault();
        }

        /// <summary>
        /// 공격 관련 버프인지 확인
        /// </summary>
        private static bool IsAttackBuff(AbilityData ability)
        {
            var timing = AbilityDatabase.GetTiming(ability);
            return timing == AbilityDatabase.AbilityTiming.PreAttackBuff;
        }

        /// <summary>
        /// AP 경제 계산
        /// </summary>
        private static void CalculateAPEconomy(TurnSituation situation, ActionContext ctx)
        {
            // 공격 비용
            situation.PrimaryAttackCost = situation.PrimaryAttack != null
                ? GameAPI.GetAbilityAPCost(situation.PrimaryAttack)
                : 1f;

            // 버프 비용
            situation.BestBuffCost = situation.BestBuff != null
                ? GameAPI.GetAbilityAPCost(situation.BestBuff)
                : 0f;

            // 공격 횟수 계산
            if (situation.PrimaryAttackCost > 0)
            {
                situation.AttacksWithoutBuff = (int)(situation.CurrentAP / situation.PrimaryAttackCost);

                float apAfterBuff = situation.CurrentAP - situation.BestBuffCost;
                situation.AttacksWithBuff = apAfterBuff > 0
                    ? (int)(apAfterBuff / situation.PrimaryAttackCost)
                    : 0;
            }
        }

        /// <summary>
        /// 타겟 분석
        /// </summary>
        private static void AnalyzeTargets(TurnSituation situation, ActionContext ctx)
        {
            if (situation.BestTarget == null) return;

            // 버프 없이 처치 가능한지 계산
            int targetHP = situation.BestTarget.Health?.HitPointsLeft ?? 100;
            float estimatedDamage = EstimateDamagePerAttack(ctx);

            situation.CanKillBestTarget =
                (estimatedDamage * situation.AttacksWithoutBuff) >= targetHP;
        }

        /// <summary>
        /// 공격당 예상 피해 추정
        /// </summary>
        private static float EstimateDamagePerAttack(ActionContext ctx)
        {
            // 레벨 기반 추정 (실제 게임 API 없으므로)
            int level = ctx.Unit.Progression?.CharacterLevel ?? 10;
            return level * 3f + 15f;  // 대략 45~75 피해
        }

        #endregion

        #region Plan Evaluation

        /// <summary>
        /// 계획 생성 및 최적 선택
        /// </summary>
        private static TurnPlan EvaluateAndSelectPlan(TurnSituation situation, ActionContext ctx)
        {
            // 긴급 상황 체크
            if (ctx.HPPercent < 30f)
            {
                return new TurnPlan
                {
                    Priority = TurnPriority.Emergency,
                    Reason = "HP critical - emergency first"
                };
            }

            // 후퇴 필요 체크
            if (situation.IsInDanger && ctx.CanMove)
            {
                return new TurnPlan
                {
                    Priority = TurnPriority.Retreat,
                    ShouldRetreat = true,
                    Reason = $"In danger (enemy {situation.NearestEnemyDistance:F1}m) + PreferRanged"
                };
            }

            // 이동 필요 체크 (공격 불가)
            if (situation.NeedsReposition)
            {
                // 버프 → 이동 → 공격 vs 이동 → 공격
                bool shouldBuffBeforeMove = ShouldBuffBeforeMove(situation, ctx);

                return new TurnPlan
                {
                    Priority = shouldBuffBeforeMove ? TurnPriority.BuffMoveAttack : TurnPriority.MoveAndAttack,
                    ShouldMoveFirst = !shouldBuffBeforeMove,
                    ShouldBuffFirst = shouldBuffBeforeMove,
                    RecommendedBuff = shouldBuffBeforeMove ? situation.BestBuff : null,
                    Reason = situation.HittableEnemies.Count == 0
                        ? "No hittable targets - move needed"
                        : "Reposition for better attack"
                };
            }

            // 공격 가능 - 버프 가치 평가
            bool shouldBuff = ShouldBuffBeforeAttack(situation, ctx);

            if (shouldBuff)
            {
                return new TurnPlan
                {
                    Priority = TurnPriority.BuffedAttack,
                    ShouldBuffFirst = true,
                    RecommendedBuff = situation.BestBuff,
                    RecommendedAttack = situation.PrimaryAttack,
                    RecommendedTarget = situation.BestTarget,
                    Reason = GetBuffReason(situation)
                };
            }

            // 직접 공격
            return new TurnPlan
            {
                Priority = TurnPriority.DirectAttack,
                ShouldBuffFirst = false,
                RecommendedAttack = situation.PrimaryAttack,
                RecommendedTarget = situation.BestTarget,
                Reason = GetDirectAttackReason(situation)
            };
        }

        /// <summary>
        /// 버프 후 공격이 더 효율적인지 판단
        /// </summary>
        private static bool ShouldBuffBeforeAttack(TurnSituation situation, ActionContext ctx)
        {
            // 버프 없음
            if (situation.BestBuff == null) return false;

            // 버프 후 공격 불가
            if (situation.AttacksWithBuff <= 0) return false;

            // 적 1명 + 버프 없이 처치 가능 = 버프 불필요
            if (situation.AllEnemies.Count == 1 && situation.CanKillBestTarget)
            {
                Main.LogDebug($"[TurnPlanner] Skip buff: can kill {situation.BestTarget?.CharacterName} without buff");
                return false;
            }

            // AP 효율 비교
            // 버프 효과 추정 (약 +20% 피해)
            const float BUFF_MULTIPLIER = 1.2f;

            float valueWithoutBuff = situation.AttacksWithoutBuff * 1.0f;
            float valueWithBuff = situation.AttacksWithBuff * BUFF_MULTIPLIER;

            // 버프 가치가 더 높으면 버프 사용
            bool buffWorthIt = valueWithBuff > valueWithoutBuff;

            Main.LogDebug($"[TurnPlanner] Buff eval: without={situation.AttacksWithoutBuff}x1.0={valueWithoutBuff:F1}, " +
                         $"with={situation.AttacksWithBuff}x{BUFF_MULTIPLIER}={valueWithBuff:F1} → " +
                         $"{(buffWorthIt ? "BUFF" : "NO BUFF")}");

            return buffWorthIt;
        }

        /// <summary>
        /// 이동 전 버프 사용 여부 판단
        /// </summary>
        private static bool ShouldBuffBeforeMove(TurnSituation situation, ActionContext ctx)
        {
            // 버프 없음
            if (situation.BestBuff == null) return false;

            // 이동 후 공격할 AP가 없으면 버프 스킵
            // 이동 = 약 1~2 AP, 공격 = 1 AP 가정
            float apAfterBuffAndMove = situation.CurrentAP - situation.BestBuffCost - 1.5f;
            if (apAfterBuffAndMove < situation.PrimaryAttackCost)
            {
                return false;
            }

            // 멀티턴 버프면 이동 전에 사용 가치 있음
            // 단, 현재 구현에서는 단순화
            return false;  // 보수적: 이동 우선
        }

        /// <summary>
        /// 버프 사용 이유 생성
        /// </summary>
        private static string GetBuffReason(TurnSituation situation)
        {
            if (situation.AllEnemies.Count > 1)
                return $"Multiple enemies ({situation.AllEnemies.Count}) - buff value high";
            if (situation.AttacksWithBuff >= 2)
                return $"Can attack {situation.AttacksWithBuff}x after buff";
            return "Buff provides better efficiency";
        }

        /// <summary>
        /// 직접 공격 이유 생성
        /// </summary>
        private static string GetDirectAttackReason(TurnSituation situation)
        {
            if (situation.CanKillBestTarget)
                return $"Can kill {situation.BestTarget?.CharacterName} without buff";
            if (situation.BestBuff == null)
                return "No buffs available";
            if (situation.AttacksWithBuff <= 0)
                return "Not enough AP for buff + attack";
            return "Direct attack more efficient";
        }

        #endregion

        #region Logging

        private static void LogPlan(string unitName, TurnPlan plan, TurnSituation situation)
        {
            Main.Log($"[TurnPlanner] ★ {unitName}: Priority={plan.Priority}, " +
                    $"Buff={plan.ShouldBuffFirst}, Move={plan.ShouldMoveFirst}, " +
                    $"Retreat={plan.ShouldRetreat}");
            Main.Log($"[TurnPlanner]   Reason: {plan.Reason}");
            Main.LogDebug($"[TurnPlanner]   AP={situation.CurrentAP:F1}, " +
                         $"Attacks: {situation.AttacksWithoutBuff}→{situation.AttacksWithBuff} with buff, " +
                         $"Hittable: {situation.HittableEnemies?.Count ?? 0}/{situation.AllEnemies?.Count ?? 0}");
        }

        #endregion
    }
}
