using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_v2.Core
{
    /// <summary>
    /// v2.1.0: 게임 API 래퍼 - 게임에게 직접 물어보기
    /// 휴리스틱 추측 대신 게임의 정확한 정보 활용
    /// </summary>
    public static class GameAPI
    {
        #region Ability Checks - 게임 API 직접 호출

        /// <summary>
        /// 능력을 타겟에게 사용할 수 있는지 게임에게 직접 물어봄
        /// </summary>
        public static bool CanUseAbilityOn(AbilityData ability, TargetWrapper target, out string reason)
        {
            reason = null;

            if (ability == null || target == null)
            {
                reason = "Null ability or target";
                return false;
            }

            try
            {
                // 게임 API 직접 호출!
                AbilityData.UnavailabilityReasonType? unavailableReason;
                bool canTarget = ability.CanTarget(target, out unavailableReason);

                if (!canTarget && unavailableReason.HasValue)
                {
                    reason = unavailableReason.Value.ToString();
                }

                return canTarget;
            }
            catch (Exception ex)
            {
                reason = $"Exception: {ex.Message}";
                return false;
            }
        }

        // CanUseAbilityFromPosition - 이동 후 공격 시뮬레이션은 게임 AI에 위임
        // 복잡한 의존성 때문에 v2.1.0에서는 단순화

        /// <summary>
        /// 능력이 현재 사용 가능한지 확인 (쿨다운, 자원 등)
        /// </summary>
        public static bool IsAbilityAvailable(AbilityData ability, out List<string> reasons)
        {
            reasons = new List<string>();

            if (ability == null)
            {
                reasons.Add("Null ability");
                return false;
            }

            try
            {
                var unavailabilityReasons = ability.GetUnavailabilityReasons();

                if (unavailabilityReasons.Count > 0)
                {
                    reasons = unavailabilityReasons.Select(r => r.ToString()).ToList();
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                reasons.Add($"Exception: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Unit State Checks - 유닛 상태 확인

        /// <summary>
        /// 유닛이 이동 가능한지 확인
        /// </summary>
        public static bool CanMove(BaseUnitEntity unit)
        {
            if (unit == null) return false;

            try
            {
                return unit.State.CanMove;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 유닛이 행동 가능한지 확인
        /// </summary>
        public static bool CanAct(BaseUnitEntity unit)
        {
            if (unit == null) return false;

            try
            {
                return unit.State.CanActInTurnBased;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 유닛의 HP 비율 (0-100)
        /// </summary>
        public static float GetHPPercent(BaseUnitEntity unit)
        {
            if (unit == null) return 0f;

            try
            {
                return (float)unit.Health.HitPointsLeft / unit.Health.MaxHitPoints * 100f;
            }
            catch
            {
                return 100f;
            }
        }

        /// <summary>
        /// 유닛이 근접 교전 중인지 확인
        /// </summary>
        public static bool IsInMeleeRange(BaseUnitEntity unit, BaseUnitEntity target)
        {
            if (unit == null || target == null) return false;

            try
            {
                float distance = Vector3.Distance(unit.Position, target.Position);
                return distance <= 3.0f; // 게임의 근접 범위
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 두 유닛 간의 거리
        /// </summary>
        public static float GetDistance(BaseUnitEntity from, BaseUnitEntity to)
        {
            if (from == null || to == null) return float.MaxValue;

            try
            {
                return Vector3.Distance(from.Position, to.Position);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        #endregion

        #region Ability Classification - 능력 분류 (게임 데이터 기반)

        /// <summary>
        /// 능력이 근접 공격인지 확인
        /// </summary>
        public static bool IsMeleeAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                return ability.IsMelee;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 능력이 원거리 공격인지 확인
        /// </summary>
        public static bool IsRangedAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                return !ability.IsMelee && ability.Blueprint.Range != AbilityRange.Personal;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 능력이 AoE인지 확인
        /// </summary>
        public static bool IsAoEAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                return ability.IsAOE || ability.GetPatternSettings() != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 능력이 자신에게 사용하는 것인지 확인
        /// </summary>
        public static bool IsSelfTargetAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                return ability.Blueprint.Range == AbilityRange.Personal ||
                       ability.TargetAnchor == AbilityTargetAnchor.Owner;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 능력이 적만 타겟 가능한지 확인
        /// </summary>
        public static bool IsOffensiveAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                var bp = ability.Blueprint;
                return bp.CanTargetEnemies && !bp.CanTargetFriends;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 능력이 아군만 타겟 가능한지 확인
        /// </summary>
        public static bool IsSupportAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                var bp = ability.Blueprint;
                return bp.CanTargetFriends && !bp.CanTargetEnemies;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 능력의 사거리 (셀 단위)
        /// </summary>
        public static int GetAbilityRange(AbilityData ability)
        {
            if (ability == null) return 0;

            try
            {
                return ability.RangeCells;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

        #region Danger Detection - 위험 감지

        /// <summary>
        /// HP를 소모하는 스킬인지 확인 (확장된 버전)
        /// Bladedancer: Blood Oath, Oath of Vengeance, Veil of Blades
        /// Master Tactician: Fervour
        /// Executioner: Reckless Abandon, Exsanguination
        /// </summary>
        public static bool IsHPCostAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // Bladedancer HP 소모 스킬
            if (name.Contains("oath") || name.Contains("맹세") ||
                name.Contains("blood") || name.Contains("피의") ||
                name.Contains("vengeance") || name.Contains("복수") ||
                name.Contains("veil of blades") || name.Contains("검의 장막") ||
                bpName.Contains("oath") || bpName.Contains("blood") ||
                bpName.Contains("vengeance") || bpName.Contains("veilofblades"))
            {
                return true;
            }

            // Master Tactician: Fervour (열정) - 20% max wounds
            if (name.Contains("fervour") || name.Contains("열정") ||
                bpName.Contains("fervour"))
            {
                return true;
            }

            // Executioner: Reckless Abandon, Exsanguination
            if (name.Contains("reckless abandon") || name.Contains("무모한 포기") ||
                name.Contains("exsanguination") || name.Contains("방혈") ||
                bpName.Contains("recklessabandon") || bpName.Contains("exsanguination"))
            {
                return true;
            }

            // 일반 키워드
            if (name.Contains("sacrifice") || name.Contains("희생") ||
                bpName.Contains("sacrifice") || bpName.Contains("wound"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 능력이 아군을 칠 수 있는 위험한 AoE인지 확인
        /// (Scatter, BladeDance 등 TargetType.Any를 사용하는 능력)
        /// </summary>
        public static bool IsDangerousAoE(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                var components = ability.Blueprint.ComponentsArray;
                foreach (var comp in components)
                {
                    string typeName = comp.GetType().Name;

                    // 디컴파일 분석 결과: 이 컴포넌트들은 TargetType.Any 사용
                    if (typeName.Contains("Scatter") ||
                        typeName.Contains("BladeDance") ||
                        typeName.Contains("CustomRam") ||
                        typeName.Contains("StepThrough"))
                    {
                        return true;
                    }
                }

                // 이름 기반 추가 체크
                string abilityName = ability.Blueprint.name?.ToLower() ?? "";
                string displayName = ability.Name?.ToLower() ?? "";

                // Bladedance - 아군 포함 공격
                if (abilityName.Contains("bladedance") || displayName.Contains("칼날춤") ||
                    displayName.Contains("bladedance"))
                {
                    return true;
                }

                // Ultimate 스킬
                if (abilityName.Contains("ultimate"))
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// AoE 패턴 내의 아군 수 계산 (단순화된 거리 기반 버전)
        /// </summary>
        public static int CountAlliesInPattern(AbilityData ability, TargetWrapper target, List<BaseUnitEntity> allies)
        {
            if (ability == null || target == null || allies == null) return 0;

            try
            {
                // 타겟 위치 가져오기
                Vector3 targetPos;
                if (target.Entity != null)
                {
                    targetPos = target.Entity.Position;
                }
                else if (target.Point != Vector3.zero)
                {
                    targetPos = target.Point;
                }
                else
                {
                    return 0;
                }

                // AoE 반경 추정 (기본 5 유닛)
                // 정확한 패턴 계산은 복잡하므로 단순한 거리 기반 휴리스틱 사용
                float aoeRadius = 5f;

                // AoE 능력인 경우 더 넓은 반경 사용
                if (IsAoEAbility(ability))
                {
                    aoeRadius = 7f;
                }

                int count = 0;
                foreach (var ally in allies)
                {
                    if (ally == null || ally == ability.Caster) continue;

                    float distance = Vector3.Distance(ally.Position, targetPos);
                    if (distance <= aoeRadius)
                    {
                        count++;
                    }
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        #endregion

        #region Target Selection Helpers

        /// <summary>
        /// 가장 가까운 적 찾기
        /// </summary>
        public static BaseUnitEntity FindNearestEnemy(BaseUnitEntity unit, List<BaseUnitEntity> enemies)
        {
            if (unit == null || enemies == null || enemies.Count == 0) return null;

            BaseUnitEntity nearest = null;
            float minDistance = float.MaxValue;

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.LifeState.IsDead) continue;

                float distance = GetDistance(unit, enemy);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = enemy;
                }
            }

            return nearest;
        }

        /// <summary>
        /// 가장 약한 적 찾기 (HP 비율 기준)
        /// </summary>
        public static BaseUnitEntity FindWeakestEnemy(List<BaseUnitEntity> enemies)
        {
            if (enemies == null || enemies.Count == 0) return null;

            BaseUnitEntity weakest = null;
            float minHP = float.MaxValue;

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.LifeState.IsDead) continue;

                float hp = GetHPPercent(enemy);
                if (hp < minHP)
                {
                    minHP = hp;
                    weakest = enemy;
                }
            }

            return weakest;
        }

        /// <summary>
        /// 가장 상처입은 아군 찾기
        /// </summary>
        public static BaseUnitEntity FindMostWoundedAlly(BaseUnitEntity unit, List<BaseUnitEntity> allies)
        {
            if (allies == null || allies.Count == 0) return null;

            BaseUnitEntity mostWounded = null;
            float minHP = 100f;

            foreach (var ally in allies)
            {
                if (ally == null || ally.LifeState.IsDead || ally == unit) continue;

                float hp = GetHPPercent(ally);
                if (hp < minHP && hp < 100f) // 상처입은 아군만
                {
                    minHP = hp;
                    mostWounded = ally;
                }
            }

            return mostWounded;
        }

        #endregion

        #region Veil Degradation - Psyker 안전 관리

        // Veil 임계값 상수
        public const int VEIL_WARNING_THRESHOLD = 10;   // 이 이상이면 Major 사이킥 제한 시작
        public const int VEIL_DANGER_THRESHOLD = 15;    // 이 이상이면 Major 사이킥 완전 차단
        public const int VEIL_MAXIMUM = 20;              // 최대 Veil 값

        /// <summary>
        /// 현재 Veil Degradation 레벨 가져오기
        /// </summary>
        public static int GetCurrentVeil()
        {
            try
            {
                return Game.Instance?.TurnController?.VeilThicknessCounter?.Value ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 능력이 사이킥 파워인지 확인
        /// </summary>
        public static bool IsPsychicAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                return ability.Blueprint?.IsPsykerAbility ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 능력이 Major 사이킥 파워인지 확인 (Veil +3)
        /// VeilThicknessPointsToAdd >= 3 이면 Major
        /// </summary>
        public static bool IsMajorPsychicAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                var bp = ability.Blueprint;
                if (bp == null) return false;

                // Major 사이킥은 VeilThicknessPointsToAdd가 3 이상
                return bp.IsPsykerAbility && bp.VeilThicknessPointsToAdd >= 3;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 능력이 Minor 사이킥 파워인지 확인 (Veil +1)
        /// VeilThicknessPointsToAdd < 3 이면 Minor
        /// </summary>
        public static bool IsMinorPsychicAbility(AbilityData ability)
        {
            if (ability == null) return false;

            try
            {
                var bp = ability.Blueprint;
                if (bp == null) return false;

                // Minor 사이킥은 VeilThicknessPointsToAdd가 3 미만
                return bp.IsPsykerAbility && bp.VeilThicknessPointsToAdd < 3;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 사이킥 능력 사용 시 증가할 Veil 값
        /// </summary>
        public static int GetVeilIncrease(AbilityData ability)
        {
            if (ability == null) return 0;

            try
            {
                if (!IsPsychicAbility(ability)) return 0;

                // BlueprintAbility의 VeilThicknessPointsToAdd 값 직접 사용
                return ability.Blueprint?.VeilThicknessPointsToAdd ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 사이킥 능력 사용이 안전한지 평가
        /// </summary>
        /// <returns>
        /// Safe: 자유롭게 사용 가능
        /// Caution: 사용 가능하지만 주의 필요 (Veil 10-14에서 Major)
        /// Dangerous: 사용하면 Perils of the Warp 위험 (Veil 15+ 도달)
        /// Blocked: 사용 금지 (현재 Veil 15+ 상태에서 Major)
        /// </returns>
        public static PsychicSafetyLevel EvaluatePsychicSafety(AbilityData ability)
        {
            if (!IsPsychicAbility(ability))
            {
                return PsychicSafetyLevel.Safe; // 사이킥 아니면 안전
            }

            int currentVeil = GetCurrentVeil();
            int veilIncrease = GetVeilIncrease(ability);
            int projectedVeil = currentVeil + veilIncrease;

            // Major 사이킥인 경우
            if (IsMajorPsychicAbility(ability))
            {
                // 현재 Veil이 이미 15 이상이면 완전 차단
                if (currentVeil >= VEIL_DANGER_THRESHOLD)
                {
                    return PsychicSafetyLevel.Blocked;
                }

                // 사용 후 Veil이 15 이상 되면 위험
                if (projectedVeil >= VEIL_DANGER_THRESHOLD)
                {
                    return PsychicSafetyLevel.Dangerous;
                }

                // 현재 Veil이 10 이상이면 주의
                if (currentVeil >= VEIL_WARNING_THRESHOLD)
                {
                    return PsychicSafetyLevel.Caution;
                }
            }
            // Minor 사이킥인 경우
            else
            {
                // Minor는 Veil +1이므로 15에서만 위험
                if (projectedVeil >= VEIL_MAXIMUM)
                {
                    return PsychicSafetyLevel.Dangerous;
                }
            }

            return PsychicSafetyLevel.Safe;
        }

        /// <summary>
        /// Veil 상태 요약 로그용
        /// </summary>
        public static string GetVeilStatusString()
        {
            int veil = GetCurrentVeil();
            string status = veil >= VEIL_DANGER_THRESHOLD ? "DANGER" :
                           veil >= VEIL_WARNING_THRESHOLD ? "WARNING" : "SAFE";
            return $"Veil={veil}/{VEIL_MAXIMUM} ({status})";
        }

        #endregion

        #region Momentum System - 파티 모멘텀 관리

        // Momentum 임계값 상수
        public const int MOMENTUM_MIN = 0;
        public const int MOMENTUM_MAX = 200;
        public const int MOMENTUM_START = 100;
        public const int MOMENTUM_HEROIC_THRESHOLD = 175;    // Heroic Act 가능
        public const int MOMENTUM_DESPERATE_THRESHOLD = 50;  // Desperate Measures 영역

        /// <summary>
        /// 파티의 현재 Momentum 가져오기
        /// </summary>
        public static int GetCurrentMomentum()
        {
            try
            {
                var momentumGroups = Game.Instance?.Player?.GetOrCreate<Kingmaker.Controllers.TurnBased.TurnDataPart>()?.MomentumGroups;
                if (momentumGroups == null) return MOMENTUM_START;

                // 파티 그룹 찾기
                foreach (var group in momentumGroups)
                {
                    if (group.IsParty)
                    {
                        return group.Momentum;
                    }
                }

                return MOMENTUM_START;
            }
            catch
            {
                return MOMENTUM_START;
            }
        }

        /// <summary>
        /// Heroic Act이 사용 가능한지 (Momentum >= 175)
        /// </summary>
        public static bool IsHeroicActAvailable()
        {
            return GetCurrentMomentum() >= MOMENTUM_HEROIC_THRESHOLD;
        }

        /// <summary>
        /// Desperate Measures 영역인지 (Momentum 낮음)
        /// </summary>
        public static bool IsDesperateMeasures()
        {
            return GetCurrentMomentum() <= MOMENTUM_DESPERATE_THRESHOLD;
        }

        /// <summary>
        /// Momentum 상태 요약 로그용
        /// </summary>
        public static string GetMomentumStatusString()
        {
            int momentum = GetCurrentMomentum();
            string status = momentum >= MOMENTUM_HEROIC_THRESHOLD ? "HEROIC" :
                           momentum <= MOMENTUM_DESPERATE_THRESHOLD ? "DESPERATE" : "NORMAL";
            return $"Momentum={momentum}/{MOMENTUM_MAX} ({status})";
        }

        #endregion

        #region Ability Type Detection - 스킬 유형 감지

        /// <summary>
        /// 모멘텀을 생성하는 스킬인지 확인
        /// War Hymn, Assign Objective, Inspire 등
        /// </summary>
        public static bool IsMomentumGeneratingAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // War Hymn (Ministorum Priest)
            if (name.Contains("war hymn") || name.Contains("전쟁 찬가") ||
                bpName.Contains("warhymn"))
            {
                return true;
            }

            // Assign Objective (Master Tactician)
            if (name.Contains("assign objective") || name.Contains("목표 지정") ||
                bpName.Contains("assignobjective"))
            {
                return true;
            }

            // Inspire (Master Tactician) - 조건부 모멘텀 회복
            if (name.Contains("inspire") || name.Contains("고무") ||
                bpName.Contains("inspire"))
            {
                return true;
            }

            // Bring It Down! (Officer) - 조건부 모멘텀
            if (name.Contains("bring it down") || name.Contains("쓰러뜨려") ||
                bpName.Contains("bringitdown"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 방어 자세 스킬인지 확인
        /// Defensive Stance, Brace for Impact 등
        /// </summary>
        public static bool IsDefensiveStanceAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // Defensive Stance (Vanguard)
            if (name.Contains("defensive stance") || name.Contains("방어 자세") ||
                bpName.Contains("defensivestance"))
            {
                return true;
            }

            // Brace for Impact! (Navy Officer)
            if (name.Contains("brace for impact") || name.Contains("충격 대비") ||
                bpName.Contains("braceforimpact"))
            {
                return true;
            }

            // Shield Wall 등 기타 방어 스킬
            if (name.Contains("shield wall") || name.Contains("방패벽") ||
                name.Contains("hold the line") || name.Contains("사수") ||
                bpName.Contains("shieldwall") || bpName.Contains("holdline"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Righteous Fury / 킬 기반 스킬인지 확인
        /// Revel in Slaughter 등 - 적 처치 후 활성화되는 스킬
        /// </summary>
        public static bool IsRighteousFuryAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // Revel in Slaughter (Soldier) - 적 3명 처치 후 활성화
            if (name.Contains("revel in slaughter") || name.Contains("학살의 환희") ||
                bpName.Contains("revelinslaughter"))
            {
                return true;
            }

            // Holy Rage 등 분노 관련 스킬
            if (name.Contains("holy rage") || name.Contains("신성한 분노") ||
                name.Contains("righteous fury") || name.Contains("정의로운 분노") ||
                bpName.Contains("holyrage") || bpName.Contains("righteousfury"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Heroic Act 스킬인지 확인 (Momentum 175+ 필요)
        /// </summary>
        public static bool IsHeroicActAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // Heroic Act 관련 키워드
            if (name.Contains("heroic") || name.Contains("영웅적") ||
                bpName.Contains("heroic") || bpName.Contains("heroicact"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Desperate Measure 스킬인지 확인 (Momentum 낮을 때 사용)
        /// </summary>
        public static bool IsDesperateMeasureAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // Desperate Measure 관련 키워드
            if (name.Contains("desperate") || name.Contains("필사적") ||
                name.Contains("last stand") || name.Contains("최후의 저항") ||
                bpName.Contains("desperate") || bpName.Contains("laststand"))
            {
                return true;
            }

            return false;
        }

        #endregion
    }

    /// <summary>
    /// 사이킥 능력 사용 안전 수준
    /// </summary>
    public enum PsychicSafetyLevel
    {
        Safe,       // 안전하게 사용 가능
        Caution,    // 사용 가능하지만 Veil이 높음 (10-14에서 Major 사용)
        Dangerous,  // 사용하면 Perils of the Warp 위험 영역 진입
        Blocked     // 이미 위험 영역, Major 사용 금지
    }
}
