using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.Utility;
using UnityEngine;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.0: 게임 API 래퍼 - 게임에게 직접 물어보기
    /// AbilityRules 시스템과 통합
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

        /// <summary>
        /// ★ v2.2.1: 사거리 문제로 실패했는지 확인
        /// </summary>
        public static bool IsOutOfRangeOnly(AbilityData ability, TargetWrapper target)
        {
            if (ability == null || target == null) return false;

            try
            {
                AbilityData.UnavailabilityReasonType? unavailableReason;
                bool canTarget = ability.CanTarget(target, out unavailableReason);

                if (!canTarget && unavailableReason.HasValue)
                {
                    // 사거리 관련 이유인지 확인
                    string reason = unavailableReason.Value.ToString().ToLower();
                    return reason.Contains("range") || reason.Contains("distance") ||
                           reason.Contains("reach") || reason.Contains("far");
                }

                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// ★ v2.2.1: 이동 가능 거리 내에 있는지 확인
        /// </summary>
        public static bool IsWithinMoveRange(BaseUnitEntity unit, BaseUnitEntity target)
        {
            if (unit == null || target == null) return false;

            try
            {
                float distance = GetDistance(unit, target);
                // 이동 + 무기 사거리 (보수적 추정: 20m)
                // 게임에서 일반적으로 이동 7-10m + 무기 사거리 5-15m
                return distance <= 20f;
            }
            catch { return true; } // 오류 시 가능하다고 가정
        }

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
                // ★ v2.2.2: 소모품 충전 횟수 체크 (charges=0이면 사용 불가)
                if (ability.SourceItem != null)
                {
                    var usableItem = ability.SourceItem as Kingmaker.Items.ItemEntityUsable;
                    if (usableItem != null && usableItem.Charges <= 0)
                    {
                        reasons.Add("No charges remaining");
                        return false;
                    }
                }

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

        #region Unit State Checks

        public static bool CanMove(BaseUnitEntity unit)
        {
            if (unit == null) return false;
            try { return unit.State.CanMove; }
            catch { return false; }
        }

        public static bool CanAct(BaseUnitEntity unit)
        {
            if (unit == null) return false;
            try { return unit.State.CanActInTurnBased; }
            catch { return false; }
        }

        public static float GetHPPercent(BaseUnitEntity unit)
        {
            if (unit == null) return 0f;
            try { return (float)unit.Health.HitPointsLeft / unit.Health.MaxHitPoints * 100f; }
            catch { return 100f; }
        }

        /// <summary>
        /// 능력에서 발생하는 버프가 이미 유닛에 활성화되어 있는지 확인
        /// </summary>
        public static bool HasActiveBuff(BaseUnitEntity unit, AbilityData ability)
        {
            if (unit == null || ability == null) return false;

            try
            {
                string abilityBpName = ability.Blueprint?.name ?? "";
                if (string.IsNullOrEmpty(abilityBpName)) return false;

                foreach (var buff in unit.Buffs.Enumerable)
                {
                    try
                    {
                        string buffBpName = buff.Blueprint?.name ?? "";
                        if (!string.IsNullOrEmpty(buffBpName) && buffBpName == abilityBpName)
                        {
                            Main.LogDebug($"[GameAPI] Buff already active: {ability.Name} on {unit.CharacterName}");
                            return true;
                        }
                    }
                    catch { /* 개별 버프 체크 오류 무시 */ }
                }

                return false;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] HasActiveBuff error: {ex.Message}");
                return false;
            }
        }

        public static bool HasActiveBuffOnTarget(BaseUnitEntity target, AbilityData ability)
        {
            return HasActiveBuff(target, ability);
        }

        public static bool IsInMeleeRange(BaseUnitEntity unit, BaseUnitEntity target)
        {
            if (unit == null || target == null) return false;
            try
            {
                float distance = Vector3.Distance(unit.Position, target.Position);
                return distance <= 3.0f;
            }
            catch { return false; }
        }

        public static float GetDistance(BaseUnitEntity from, BaseUnitEntity to)
        {
            if (from == null || to == null) return float.MaxValue;
            try { return Vector3.Distance(from.Position, to.Position); }
            catch { return float.MaxValue; }
        }

        #endregion

        #region Ability Classification

        public static bool IsMeleeAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try { return ability.IsMelee; }
            catch { return false; }
        }

        public static bool IsRangedAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try { return !ability.IsMelee && ability.Blueprint.Range != AbilityRange.Personal; }
            catch { return false; }
        }

        public static bool IsAoEAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try { return ability.IsAOE || ability.GetPatternSettings() != null; }
            catch { return false; }
        }

        public static bool IsSelfTargetAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try
            {
                return ability.Blueprint.Range == AbilityRange.Personal ||
                       ability.TargetAnchor == AbilityTargetAnchor.Owner;
            }
            catch { return false; }
        }

        public static bool IsOffensiveAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try
            {
                var bp = ability.Blueprint;
                return bp.CanTargetEnemies && !bp.CanTargetFriends;
            }
            catch { return false; }
        }

        public static bool IsSupportAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try
            {
                var bp = ability.Blueprint;
                return bp.CanTargetFriends && !bp.CanTargetEnemies;
            }
            catch { return false; }
        }

        public static int GetAbilityRange(AbilityData ability)
        {
            if (ability == null) return 0;
            try { return ability.RangeCells; }
            catch { return 0; }
        }

        #endregion

        #region Danger Detection

        /// <summary>
        /// HP를 소모하는 스킬인지 확인
        /// </summary>
        public static bool IsHPCostAbility(AbilityData ability)
        {
            if (ability == null) return false;

            // AbilityRules 시스템 체크
            var timing = AbilityRulesDatabase.GetTiming(ability);
            if (timing == AbilityTiming.SelfDamage)
                return true;

            // 폴백: 이름 기반 체크
            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // ★ v2.2.2: 소모품/앰플은 HP 소모 스킬이 아님
            if (name.Contains("ampoule") || name.Contains("앰플") ||
                name.Contains("vial") || name.Contains("potion") ||
                name.Contains("grenade") || name.Contains("수류탄") ||
                bpName.Contains("ampoule") || bpName.Contains("vial") ||
                bpName.Contains("consumable") || bpName.Contains("throwable"))
                return false;

            return name.Contains("oath") || name.Contains("blood") ||
                   name.Contains("vengeance") || name.Contains("sacrifice") ||
                   name.Contains("fervour") || name.Contains("reckless") ||
                   name.Contains("exsanguination") ||
                   bpName.Contains("oath") || bpName.Contains("blood") ||
                   bpName.Contains("sacrifice") || bpName.Contains("fervour");
        }

        /// <summary>
        /// 능력이 아군을 칠 수 있는 위험한 AoE인지 확인
        /// </summary>
        public static bool IsDangerousAoE(AbilityData ability)
        {
            if (ability == null) return false;

            // AbilityRules 시스템 체크
            var timing = AbilityRulesDatabase.GetTiming(ability);
            if (timing == AbilityTiming.DangerousAoE)
                return true;

            try
            {
                var components = ability.Blueprint.ComponentsArray;
                foreach (var comp in components)
                {
                    string typeName = comp.GetType().Name;
                    if (typeName.Contains("Scatter") || typeName.Contains("BladeDance") ||
                        typeName.Contains("CustomRam") || typeName.Contains("StepThrough"))
                        return true;
                }

                string abilityName = ability.Blueprint.name?.ToLower() ?? "";
                if (abilityName.Contains("bladedance") || abilityName.Contains("ultimate"))
                    return true;

                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// AoE 패턴 내의 아군 수 계산
        /// </summary>
        public static int CountAlliesInPattern(AbilityData ability, TargetWrapper target, List<BaseUnitEntity> allies)
        {
            if (ability == null || target == null || allies == null) return 0;

            try
            {
                Vector3 targetPos;
                if (target.Entity != null)
                    targetPos = target.Entity.Position;
                else if (target.Point != Vector3.zero)
                    targetPos = target.Point;
                else
                    return 0;

                float aoeRadius = IsAoEAbility(ability) ? 7f : 5f;

                int count = 0;
                foreach (var ally in allies)
                {
                    if (ally == null || ally == ability.Caster) continue;
                    float distance = Vector3.Distance(ally.Position, targetPos);
                    if (distance <= aoeRadius)
                        count++;
                }

                return count;
            }
            catch { return 0; }
        }

        #endregion

        #region Target Selection Helpers

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

        public static BaseUnitEntity FindMostWoundedAlly(BaseUnitEntity unit, List<BaseUnitEntity> allies)
        {
            if (allies == null || allies.Count == 0) return null;

            BaseUnitEntity mostWounded = null;
            float minHP = 100f;

            foreach (var ally in allies)
            {
                if (ally == null || ally.LifeState.IsDead || ally == unit) continue;
                float hp = GetHPPercent(ally);
                if (hp < minHP && hp < 100f)
                {
                    minHP = hp;
                    mostWounded = ally;
                }
            }

            return mostWounded;
        }

        /// <summary>
        /// 마무리 스킬 대상 찾기 - HP가 임계값 이하인 적
        /// </summary>
        public static BaseUnitEntity FindFinisherTarget(AbilityData ability, List<BaseUnitEntity> enemies)
        {
            if (ability == null || enemies == null) return null;

            var rule = AbilityRulesDatabase.GetRule(ability);
            float threshold = rule?.TargetHPThreshold ?? 30f;

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.LifeState.IsDead) continue;
                if (GetHPPercent(enemy) <= threshold)
                    return enemy;
            }

            return null;
        }

        #endregion

        #region Veil Degradation - Psyker 안전 관리

        public const int VEIL_WARNING_THRESHOLD = 10;
        public const int VEIL_DANGER_THRESHOLD = 15;
        public const int VEIL_MAXIMUM = 20;

        public static int GetCurrentVeil()
        {
            try { return Game.Instance?.TurnController?.VeilThicknessCounter?.Value ?? 0; }
            catch { return 0; }
        }

        public static bool IsPsychicAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try { return ability.Blueprint?.IsPsykerAbility ?? false; }
            catch { return false; }
        }

        public static bool IsMajorPsychicAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try
            {
                var bp = ability.Blueprint;
                return bp != null && bp.IsPsykerAbility && bp.VeilThicknessPointsToAdd >= 3;
            }
            catch { return false; }
        }

        public static bool IsMinorPsychicAbility(AbilityData ability)
        {
            if (ability == null) return false;
            try
            {
                var bp = ability.Blueprint;
                return bp != null && bp.IsPsykerAbility && bp.VeilThicknessPointsToAdd < 3;
            }
            catch { return false; }
        }

        public static int GetVeilIncrease(AbilityData ability)
        {
            if (ability == null || !IsPsychicAbility(ability)) return 0;
            try { return ability.Blueprint?.VeilThicknessPointsToAdd ?? 0; }
            catch { return 0; }
        }

        public static PsychicSafetyLevel EvaluatePsychicSafety(AbilityData ability)
        {
            if (!IsPsychicAbility(ability))
                return PsychicSafetyLevel.Safe;

            int currentVeil = GetCurrentVeil();
            int veilIncrease = GetVeilIncrease(ability);
            int projectedVeil = currentVeil + veilIncrease;

            if (IsMajorPsychicAbility(ability))
            {
                if (currentVeil >= VEIL_DANGER_THRESHOLD)
                    return PsychicSafetyLevel.Blocked;
                if (projectedVeil >= VEIL_DANGER_THRESHOLD)
                    return PsychicSafetyLevel.Dangerous;
                if (currentVeil >= VEIL_WARNING_THRESHOLD)
                    return PsychicSafetyLevel.Caution;
            }
            else
            {
                if (projectedVeil >= VEIL_MAXIMUM)
                    return PsychicSafetyLevel.Dangerous;
            }

            return PsychicSafetyLevel.Safe;
        }

        public static string GetVeilStatusString()
        {
            int veil = GetCurrentVeil();
            string status = veil >= VEIL_DANGER_THRESHOLD ? "DANGER" :
                           veil >= VEIL_WARNING_THRESHOLD ? "WARNING" : "SAFE";
            return $"Veil={veil}/{VEIL_MAXIMUM} ({status})";
        }

        #endregion

        #region Momentum System

        public const int MOMENTUM_MIN = 0;
        public const int MOMENTUM_MAX = 200;
        public const int MOMENTUM_START = 100;
        public const int MOMENTUM_HEROIC_THRESHOLD = 175;
        public const int MOMENTUM_DESPERATE_THRESHOLD = 50;

        public static int GetCurrentMomentum()
        {
            try
            {
                var momentumGroups = Game.Instance?.Player?.GetOrCreate<Kingmaker.Controllers.TurnBased.TurnDataPart>()?.MomentumGroups;
                if (momentumGroups == null) return MOMENTUM_START;

                foreach (var group in momentumGroups)
                {
                    if (group.IsParty)
                        return group.Momentum;
                }
                return MOMENTUM_START;
            }
            catch { return MOMENTUM_START; }
        }

        public static bool IsHeroicActAvailable()
        {
            return GetCurrentMomentum() >= MOMENTUM_HEROIC_THRESHOLD;
        }

        public static bool IsDesperateMeasures()
        {
            return GetCurrentMomentum() <= MOMENTUM_DESPERATE_THRESHOLD;
        }

        public static string GetMomentumStatusString()
        {
            int momentum = GetCurrentMomentum();
            string status = momentum >= MOMENTUM_HEROIC_THRESHOLD ? "HEROIC" :
                           momentum <= MOMENTUM_DESPERATE_THRESHOLD ? "DESPERATE" : "NORMAL";
            return $"Momentum={momentum}/{MOMENTUM_MAX} ({status})";
        }

        #endregion

        #region Timing-Aware Ability Helpers

        /// <summary>
        /// 현재 컨텍스트에서 능력을 사용할 수 있는지 타이밍 체크
        /// </summary>
        public static bool CanUseAbilityAtCurrentTiming(AbilityData ability, ActionContext ctx)
        {
            if (ability == null) return false;

            var timing = AbilityRulesDatabase.GetTiming(ability);
            var rule = AbilityRulesDatabase.GetRule(ability);

            switch (timing)
            {
                case AbilityTiming.PostFirstAction:
                    // 첫 행동 후에만 사용 가능
                    if (!ctx.HasPerformedFirstAction)
                    {
                        Main.LogDebug($"[GameAPI] {ability.Name}: PostFirstAction - waiting for first action");
                        return false;
                    }
                    return true;

                case AbilityTiming.TurnEnding:
                    // 다른 유효한 공격이 없을 때만 사용
                    // (전략에서 체크)
                    return true;

                case AbilityTiming.Finisher:
                    // 타겟 HP 체크
                    if (rule?.TargetHPThreshold > 0)
                    {
                        var target = FindFinisherTarget(ability, ctx.Enemies);
                        if (target == null)
                        {
                            Main.LogDebug($"[GameAPI] {ability.Name}: Finisher - no low HP target");
                            return false;
                        }
                    }
                    return true;

                case AbilityTiming.SelfDamage:
                    // HP 임계값 체크
                    float hpThreshold = rule?.HPThreshold ?? 50f;
                    if (ctx.HPPercent < hpThreshold)
                    {
                        Main.LogDebug($"[GameAPI] {ability.Name}: SelfDamage blocked - HP too low ({ctx.HPPercent:F0}% < {hpThreshold}%)");
                        return false;
                    }
                    return true;

                case AbilityTiming.Emergency:
                    // 자신이나 아군 HP 낮을 때만
                    if (ctx.HPPercent > 30 && (ctx.MostWoundedAlly == null || GetHPPercent(ctx.MostWoundedAlly) > 30))
                    {
                        Main.LogDebug($"[GameAPI] {ability.Name}: Emergency - not in emergency situation");
                        return false;
                    }
                    return true;

                case AbilityTiming.DangerousAoE:
                    // AoE 안전성 체크는 별도로
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>
        /// 선제적 버프 (PreCombatBuff, PreAttackBuff) 필터링
        /// </summary>
        public static List<AbilityData> FilterProactiveBuffs(List<AbilityData> abilities, BaseUnitEntity unit)
        {
            var result = new List<AbilityData>();

            foreach (var ability in abilities)
            {
                if (!AbilityRulesDatabase.IsProactiveBuff(ability))
                    continue;

                // 이미 활성화된 버프 제외
                if (HasActiveBuff(unit, ability))
                    continue;

                // 자기 타겟 능력만
                if (!IsSelfTargetAbility(ability) && !IsSupportAbility(ability))
                    continue;

                result.Add(ability);
            }

            return result;
        }

        /// <summary>
        /// PostFirstAction 스킬 필터링
        /// </summary>
        public static List<AbilityData> FilterPostFirstActionAbilities(List<AbilityData> abilities)
        {
            return abilities.Where(a => AbilityRulesDatabase.IsPostFirstAction(a)).ToList();
        }

        /// <summary>
        /// 턴 종료 스킬 필터링
        /// </summary>
        public static List<AbilityData> FilterTurnEndingAbilities(List<AbilityData> abilities)
        {
            return abilities.Where(a => AbilityRulesDatabase.IsTurnEnding(a)).ToList();
        }

        /// <summary>
        /// 마무리 스킬 필터링
        /// </summary>
        public static List<AbilityData> FilterFinisherAbilities(List<AbilityData> abilities)
        {
            return abilities.Where(a => AbilityRulesDatabase.IsFinisher(a)).ToList();
        }

        #endregion

        #region Ability Type Detection

        public static bool IsMomentumGeneratingAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            return name.Contains("war hymn") || name.Contains("assign objective") ||
                   name.Contains("inspire") || name.Contains("bring it down") ||
                   bpName.Contains("warhymn") || bpName.Contains("assignobjective") ||
                   bpName.Contains("inspire") || bpName.Contains("bringitdown");
        }

        public static bool IsDefensiveStanceAbility(AbilityData ability)
        {
            if (ability == null) return false;

            // AbilityRules 시스템 체크
            var timing = AbilityRulesDatabase.GetTiming(ability);
            if (timing == AbilityTiming.PreCombatBuff)
            {
                string bpName = ability.Blueprint?.name?.ToLower() ?? "";
                if (bpName.Contains("defensive") || bpName.Contains("stance") ||
                    bpName.Contains("brace") || bpName.Contains("shield"))
                    return true;
            }

            string name = ability.Name?.ToLower() ?? "";
            return name.Contains("defensive stance") || name.Contains("brace for impact") ||
                   name.Contains("shield wall") || name.Contains("hold the line");
        }

        public static bool IsHeroicActAbility(AbilityData ability)
        {
            if (ability == null) return false;
            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            return name.Contains("heroic") || bpName.Contains("heroic");
        }

        public static bool IsDesperateMeasureAbility(AbilityData ability)
        {
            if (ability == null) return false;
            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            return name.Contains("desperate") || name.Contains("last stand") ||
                   bpName.Contains("desperate") || bpName.Contains("laststand");
        }

        /// <summary>
        /// Righteous Fury / 킬 기반 스킬인지 확인
        /// Revel in Slaughter 등 - 적 처치 후 활성화되는 스킬
        /// </summary>
        public static bool IsRighteousFuryAbility(AbilityData ability)
        {
            if (ability == null) return false;

            // AbilityRules 시스템 체크
            if (AbilityRulesDatabase.IsRighteousFury(ability))
                return true;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // Revel in Slaughter (Soldier) - 적 3명 처치 후 활성화
            if (name.Contains("revel in slaughter") || name.Contains("학살의 환희") ||
                bpName.Contains("revelinslaughter"))
                return true;

            // Holy Rage 등 분노 관련 스킬
            if (name.Contains("holy rage") || name.Contains("신성한 분노") ||
                name.Contains("righteous fury") || name.Contains("정의로운 분노") ||
                bpName.Contains("holyrage") || bpName.Contains("righteousfury"))
                return true;

            return false;
        }

        /// <summary>
        /// 도발 스킬인지 확인
        /// </summary>
        public static bool IsTauntAbility(AbilityData ability)
        {
            if (ability == null) return false;

            // AbilityRules 시스템 체크
            if (AbilityRulesDatabase.IsTaunt(ability))
                return true;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            return name.Contains("taunt") || name.Contains("provoke") ||
                   name.Contains("도발") || name.Contains("어그로") ||
                   bpName.Contains("taunt") || bpName.Contains("provoke");
        }

        #endregion
    }

    /// <summary>
    /// 사이킥 능력 사용 안전 수준
    /// </summary>
    public enum PsychicSafetyLevel
    {
        Safe,
        Caution,
        Dangerous,
        Blocked
    }
}
