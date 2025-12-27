using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.Pathfinding;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Abilities.Components;
using Kingmaker.UnitLogic.Mechanics.Actions;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.Utility;
using Kingmaker.View.Covers;
using UnityEngine;
using static CompanionAI_v2_2.Core.AbilityDatabase;
using CompanionAI_v2_2.Settings;

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
        /// ★ v2.2.13: CanTargetFromNode 사용으로 LOS/위치 기반 검증 추가
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
                // 기본 타겟 검증
                AbilityData.UnavailabilityReasonType? unavailableReason;
                bool canTarget = ability.CanTarget(target, out unavailableReason);

                if (!canTarget && unavailableReason.HasValue)
                {
                    reason = unavailableReason.Value.ToString();
                    return false;
                }

                // ★ v2.2.13: 위치 기반 추가 검증 (LOS, 사거리 등)
                // 게임 AI의 SingleTargetSelector.SelectTarget()이 사용하는 것과 동일한 검증
                var caster = ability.Caster as BaseUnitEntity;
                var targetEntity = target.Entity as BaseUnitEntity;

                if (caster != null && targetEntity != null)
                {
                    var casterNode = caster.CurrentUnwalkableNode;
                    var targetNode = targetEntity.CurrentUnwalkableNode;

                    if (casterNode != null && targetNode != null)
                    {
                        int distance;
                        LosCalculations.CoverType coverType;

                        // CanTargetFromNode: 실제 게임이 사용하는 위치 기반 타겟 검증
                        bool canTargetFromNode = ability.CanTargetFromNode(casterNode, targetNode, target, out distance, out coverType);

                        if (!canTargetFromNode)
                        {
                            bool hasLos = coverType != LosCalculations.CoverType.Invisible;
                            reason = hasLos ? "OutOfRange" : "NoLineOfSight";
                            Main.LogDebug($"[GameAPI] CanTargetFromNode failed: {ability.Name} -> {targetEntity.CharacterName}, Cover={coverType}, Dist={distance}");
                            return false;
                        }
                    }
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
        /// ★ v2.2.39: 능력이 적용하는 버프가 이미 유닛에 활성화되어 있는지 확인
        /// 게임의 AbilityEffectRunAction → ContextActionApplyBuff 체인을 따라 실제 버프 추출
        /// </summary>
        public static bool HasActiveBuff(BaseUnitEntity unit, AbilityData ability)
        {
            if (unit == null || ability == null) return false;

            try
            {
                // ★ v2.2.39: 능력의 실행 액션에서 버프 추출
                var runAction = ability.Blueprint?.GetComponent<AbilityEffectRunAction>();
                if (runAction?.Actions?.Actions != null)
                {
                    foreach (var action in runAction.Actions.Actions)
                    {
                        // ContextActionApplyBuff 찾기
                        if (action is ContextActionApplyBuff applyBuff)
                        {
                            var buffBlueprint = applyBuff.Buff;
                            if (buffBlueprint == null) continue;

                            // 타겟에 해당 버프가 있는지 확인
                            var existingBuff = unit.Buffs.GetBuff(buffBlueprint);
                            if (existingBuff != null)
                            {
                                // Stacking 타입 확인 - Ignore/Prolong이면 재사용 불필요
                                var stacking = buffBlueprint.Stacking;
                                if (stacking == StackingType.Ignore ||
                                    stacking == StackingType.Prolong)
                                {
                                    Main.LogDebug($"[GameAPI] ★ Buff active ({stacking}): {buffBlueprint.name} on {unit.CharacterName}");
                                    return true;
                                }

                                // Rank 타입이면 최대 랭크 체크
                                if (stacking == StackingType.Rank)
                                {
                                    if (existingBuff.Rank >= buffBlueprint.MaxRank)
                                    {
                                        Main.LogDebug($"[GameAPI] ★ Buff at max rank: {buffBlueprint.name} ({existingBuff.Rank}/{buffBlueprint.MaxRank})");
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }

                // 폴백: 기존 이름 매칭 방식 (버프 컴포넌트 없는 능력용)
                string abilityBpName = ability.Blueprint?.name ?? "";
                if (!string.IsNullOrEmpty(abilityBpName))
                {
                    foreach (var buff in unit.Buffs.Enumerable)
                    {
                        try
                        {
                            string buffBpName = buff.Blueprint?.name ?? "";
                            if (!string.IsNullOrEmpty(buffBpName) && buffBpName == abilityBpName)
                            {
                                Main.LogDebug($"[GameAPI] Buff already active (name match): {ability.Name} on {unit.CharacterName}");
                                return true;
                            }
                        }
                        catch { /* 개별 버프 체크 오류 무시 */ }
                    }
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

        #region ★ v2.2.10: AP (Action Points) Management

        /// <summary>
        /// 유닛의 현재 AP 가져오기
        /// </summary>
        public static float GetCurrentAP(BaseUnitEntity unit)
        {
            if (unit == null) return 0f;
            try
            {
                // 게임의 CombatState에서 AP 확인
                var combatState = unit.CombatState;
                if (combatState != null)
                {
                    return combatState.ActionPointsBlue;
                }
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] GetCurrentAP error: {ex.Message}");
            }
            return 3f; // 기본 폴백
        }

        /// <summary>
        /// 유닛의 최대 AP 가져오기
        /// </summary>
        public static float GetMaxAP(BaseUnitEntity unit)
        {
            if (unit == null) return 3f;
            try
            {
                var combatState = unit.CombatState;
                if (combatState != null)
                {
                    // MaxActionPoints는 일반적으로 3
                    return 3f; // 기본 최대 AP
                }
            }
            catch { }
            return 3f;
        }

        /// <summary>
        /// 능력의 AP 비용 가져오기
        /// ★ v2.2.12: 게임 API 직접 사용 (AbilityData.CalculateActionPointCost)
        /// </summary>
        public static float GetAbilityAPCost(AbilityData ability)
        {
            if (ability == null) return 1f;
            try
            {
                // ★ 게임의 실제 AP 비용 계산 사용
                // RuleCalculateAbilityActionPointCost를 내부적으로 사용함
                int cost = ability.CalculateActionPointCost();
                Main.LogDebug($"[GameAPI] AP Cost for {ability.Name}: {cost}");
                return cost;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] GetAbilityAPCost error: {ex.Message}");
            }

            // 폴백: 휴리스틱
            try
            {
                string bpName = ability.Blueprint?.name?.ToLower() ?? "";
                if (bpName.Contains("heroic") || bpName.Contains("finesthour"))
                    return 3f;
                if (ability.Weapon != null)
                    return 1f;
            }
            catch { }
            return 1f;
        }

        /// <summary>
        /// 주 무기 공격 찾기 (가장 기본적인 무기 공격)
        /// </summary>
        public static AbilityData FindPrimaryWeaponAttack(List<AbilityData> abilities, bool preferRanged = false)
        {
            AbilityData bestAttack = null;
            float lowestCost = float.MaxValue;

            foreach (var ability in abilities)
            {
                if (ability.Weapon == null) continue;

                // 재장전은 제외
                if (AbilityDatabase.IsReload(ability)) continue;

                // 수류탄 제외
                if (CombatHelpers.IsGrenadeOrExplosive(ability)) continue;

                // 원거리 선호 옵션
                if (preferRanged && ability.IsMelee) continue;

                float cost = GetAbilityAPCost(ability);
                if (cost < lowestCost)
                {
                    lowestCost = cost;
                    bestAttack = ability;
                }
            }

            return bestAttack;
        }

        /// <summary>
        /// 능력 사용 후 남은 AP로 공격 가능한지 확인
        /// </summary>
        public static bool CanAffordAbilityWithReserve(ActionContext ctx, AbilityData ability, float reservedAP)
        {
            if (ability == null) return false;
            float cost = GetAbilityAPCost(ability);
            return (ctx.CurrentAP - cost) >= reservedAP;
        }

        #endregion

        #region ★ v2.2.30: Ammo Management (v2.2.31 Fix)

        /// <summary>
        /// 현재 활성 무기 가져오기 (GetFirstWeapon 로직)
        /// ★ v2.2.31: PrimaryHand → SecondaryHand → AdditionalLimbs 순서로 확인
        /// 파스칼 같은 Tech-Priest의 mechadendrite도 처리
        /// </summary>
        public static ItemEntityWeapon GetActiveWeapon(BaseUnitEntity unit)
        {
            if (unit == null) return null;

            try
            {
                var body = unit.Body;
                if (body == null) return null;

                // 1. Primary Hand
                var weapon = body.PrimaryHand?.MaybeWeapon;
                if (weapon != null) return weapon;

                // 2. Secondary Hand
                weapon = body.SecondaryHand?.MaybeWeapon;
                if (weapon != null) return weapon;

                // 3. Additional Limbs (mechadendrites 등)
                if (body.AdditionalLimbs != null)
                {
                    foreach (var limb in body.AdditionalLimbs)
                    {
                        if (limb?.MaybeWeapon != null)
                            return limb.MaybeWeapon;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] GetActiveWeapon error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 무기의 현재 탄약 수 가져오기
        /// ★ v2.2.31: GetActiveWeapon 사용 (mechadendrite 지원)
        /// </summary>
        public static int GetCurrentAmmo(BaseUnitEntity unit, bool secondaryWeapon = false)
        {
            if (unit == null) return -1;

            try
            {
                ItemEntityWeapon weapon;

                if (secondaryWeapon)
                {
                    weapon = unit.Body?.SecondaryHand?.MaybeWeapon;
                }
                else
                {
                    // ★ v2.2.31: GetActiveWeapon 사용
                    weapon = GetActiveWeapon(unit);
                }

                if (weapon == null)
                {
                    Main.LogDebug($"[GameAPI] GetCurrentAmmo: No weapon found for {unit.CharacterName}");
                    return -1;
                }

                // WarhammerMaxAmmo가 -1이면 탄약이 필요 없는 무기 (근접)
                int maxAmmo = weapon.Blueprint?.WarhammerMaxAmmo ?? -1;
                if (maxAmmo == -1)
                {
                    Main.LogDebug($"[GameAPI] GetCurrentAmmo: {weapon.Name} is melee (no ammo)");
                    return -1; // 무한 탄약
                }

                Main.LogDebug($"[GameAPI] GetCurrentAmmo: {weapon.Name} = {weapon.CurrentAmmo}/{maxAmmo}");
                return weapon.CurrentAmmo;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] GetCurrentAmmo error: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 무기의 최대 탄약 수 가져오기
        /// ★ v2.2.31: GetActiveWeapon 사용 (mechadendrite 지원)
        /// </summary>
        public static int GetMaxAmmo(BaseUnitEntity unit, bool secondaryWeapon = false)
        {
            if (unit == null) return -1;

            try
            {
                ItemEntityWeapon weapon;

                if (secondaryWeapon)
                {
                    weapon = unit.Body?.SecondaryHand?.MaybeWeapon;
                }
                else
                {
                    weapon = GetActiveWeapon(unit);
                }

                if (weapon == null) return -1;

                int maxAmmo = weapon.Blueprint?.WarhammerMaxAmmo ?? -1;
                return maxAmmo;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] GetMaxAmmo error: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// 재장전이 필요한지 확인
        /// - 탄약이 0이면 필수 재장전
        /// - 탄약이 낮으면 (25% 이하) 권장 재장전
        /// </summary>
        public static bool NeedsReload(BaseUnitEntity unit, bool secondaryWeapon = false)
        {
            int current = GetCurrentAmmo(unit, secondaryWeapon);
            int max = GetMaxAmmo(unit, secondaryWeapon);

            // 탄약이 필요 없는 무기 (-1)
            if (current < 0 || max < 0) return false;

            // 탄약이 0이면 필수
            if (current <= 0) return true;

            return false;
        }

        /// <summary>
        /// 재장전을 권장하는지 확인 (탄약 25% 이하)
        /// </summary>
        public static bool ShouldReload(BaseUnitEntity unit, bool secondaryWeapon = false)
        {
            int current = GetCurrentAmmo(unit, secondaryWeapon);
            int max = GetMaxAmmo(unit, secondaryWeapon);

            if (current < 0 || max < 0) return false;
            if (current <= 0) return true; // 필수

            // 25% 이하면 권장
            float ratio = (float)current / max;
            return ratio <= 0.25f;
        }

        /// <summary>
        /// 이미 탄약이 가득 찬 상태인지 확인
        /// </summary>
        public static bool IsFullAmmo(BaseUnitEntity unit, bool secondaryWeapon = false)
        {
            int current = GetCurrentAmmo(unit, secondaryWeapon);
            int max = GetMaxAmmo(unit, secondaryWeapon);

            if (current < 0 || max < 0) return true; // 탄약 필요 없음
            return current >= max;
        }

        /// <summary>
        /// 재장전 스킬 찾기 (deprecated - FindAvailableReloadAbility 사용 권장)
        /// </summary>
        public static AbilityData FindReloadAbility(List<AbilityData> abilities)
        {
            return FindAvailableReloadAbility(abilities);
        }

        /// <summary>
        /// ★ v2.2.32: 사용 가능한 재장전 스킬 찾기
        /// 게임의 ability.IsAvailable을 직접 사용하여 탄약 체크 포함
        /// WeaponReloadLogic.IsAvailable()이 탄약 상태를 정확히 반영함
        /// </summary>
        public static AbilityData FindAvailableReloadAbility(List<AbilityData> abilities)
        {
            foreach (var ability in abilities)
            {
                if (!AbilityDatabase.IsReload(ability)) continue;

                try
                {
                    // ★ 핵심: 게임의 IsAvailable 직접 사용
                    // WeaponReloadLogic이 탄약 체크를 처리함
                    // 탄약 가득 → false, 탄약 부족 → true
                    if (ability.IsAvailable)
                    {
                        Main.LogDebug($"[GameAPI] Reload ability available: {ability.Name}");
                        return ability;
                    }
                    else
                    {
                        Main.LogDebug($"[GameAPI] Reload ability not available (ammo full?): {ability.Name}");
                    }
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[GameAPI] Error checking reload ability: {ex.Message}");
                }
            }

            // GUID로 못 찾으면 이름 기반 폴백
            foreach (var ability in abilities)
            {
                string bpName = ability.Blueprint?.name?.ToLower() ?? "";
                if (!bpName.Contains("reload") && !bpName.Contains("재장전")) continue;

                try
                {
                    if (ability.IsAvailable)
                    {
                        Main.LogDebug($"[GameAPI] Reload ability (name-based) available: {ability.Name}");
                        return ability;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// 탄약 상태 문자열 (디버그용)
        /// </summary>
        public static string GetAmmoStatusString(BaseUnitEntity unit)
        {
            int current = GetCurrentAmmo(unit);
            int max = GetMaxAmmo(unit);

            if (current < 0 || max < 0)
                return "Ammo=N/A";

            string status = current <= 0 ? "EMPTY" :
                           current <= max * 0.25f ? "LOW" : "OK";

            return $"Ammo={current}/{max} ({status})";
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

        /// <summary>
        /// ★ v2.2.48: 능력 사거리 가져오기
        ///
        /// 게임 AI 분석 결과 (AbilityInfo.cs, AttackEffectivenessTileScorer.cs):
        /// - effectiveRange = ability.Weapon?.AttackOptimalRange ?? ability.RangeCells
        /// - 무기 있음: Weapon.AttackOptimalRange 사용 (정확한 값)
        /// - 무기 없음 (사이킥): RangeCells = 100000 (Unlimited)
        /// - 게임 AI도 Unlimited일 때 ClosinessScore ≈ 0.999로 거의 동일 → Cover/Threat가 결정
        ///
        /// 따라서 Unlimited 능력은 range 대신 LOS+Cover+SafeDistance로 위치 결정해야 함
        /// </summary>
        public static int GetAbilityRange(AbilityData ability)
        {
            if (ability == null) return 0;
            try
            {
                // ★ 게임 AI와 동일한 로직: Weapon.AttackOptimalRange 우선
                if (ability.Weapon != null)
                {
                    int optimalRange = ability.Weapon.AttackOptimalRange;
                    if (optimalRange > 0 && optimalRange < 10000)
                    {
                        Main.LogDebug($"[GameAPI] {ability.Name}: Using weapon optimal range {optimalRange} cells");
                        return optimalRange;
                    }

                    // AttackOptimalRange 없으면 AttackRange
                    int attackRange = ability.Weapon.AttackRange;
                    if (attackRange > 0 && attackRange < 10000)
                    {
                        Main.LogDebug($"[GameAPI] {ability.Name}: Using weapon attack range {attackRange} cells");
                        return attackRange;
                    }
                }

                int range = ability.RangeCells;

                // Unlimited (100000) → 특수 값 반환
                // 호출자가 이 값을 보고 range 기반 위치 계산 대신 LOS+Cover 기반으로 전환해야 함
                if (range >= 10000)
                {
                    Main.LogDebug($"[GameAPI] {ability.Name}: Unlimited range (100000) - use LOS+Cover based positioning");
                    return 100000;  // 원본 값 그대로 반환 - 호출자가 처리
                }

                return range;
            }
            catch { return 0; }
        }

        /// <summary>
        /// ★ v2.2.48: 능력이 Unlimited 사거리인지 확인
        /// Unlimited 능력은 range 기반 위치 계산이 무의미 → LOS+Cover 기반 사용
        /// </summary>
        public static bool IsUnlimitedRange(AbilityData ability)
        {
            return GetAbilityRange(ability) >= 10000;
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
            var timing = AbilityDatabase.GetTiming(ability);
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
            var timing = AbilityDatabase.GetTiming(ability);
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

            var rule = AbilityDatabase.GetRule(ability);
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

        #region ★ v2.2.9: Advanced Target Scoring System - 게임 AI 스타일 스코어링
        // ★ v2.2.19: 공격 가능한 적만 스코어링 (LOS/Range 검증 추가)

        /// <summary>
        /// 타겟 점수 정보
        /// </summary>
        public class TargetScore
        {
            public BaseUnitEntity Target;
            public float TotalScore;
            public float HPPercentScore;      // HP% 낮을수록 높음
            public float HPAbsoluteScore;     // 절대HP 낮을수록 높음 (마무리 쉬움)
            public float DistanceScore;       // 가까울수록 높음
            public float KillableBonus;       // 처치 가능하면 보너스
            public bool IsHittable;           // ★ v2.2.19: 실제로 공격 가능한지

            public override string ToString()
            {
                return $"{Target?.CharacterName}: Total={TotalScore:F1} (HP%={HPPercentScore:F1}, HPAbs={HPAbsoluteScore:F1}, Dist={DistanceScore:F1}, Kill={KillableBonus:F1}, Hit={IsHittable})";
            }
        }

        /// <summary>
        /// ★ 복합 스코어링으로 최적 타겟 찾기
        /// 게임 AI의 AttackEffectivenessTileScorer 참고
        /// ★ v2.2.19: 공격 가능한 적만 선택
        /// </summary>
        public static BaseUnitEntity FindBestTarget(BaseUnitEntity unit, List<BaseUnitEntity> enemies,
            bool preferKillable = true, bool preferClose = false)
        {
            if (unit == null || enemies == null || enemies.Count == 0) return null;

            var scores = ScoreAllTargets(unit, enemies);
            if (scores.Count == 0) return null;

            // ★ v2.2.19: 공격 가능한 적만 필터링
            var hittableScores = scores.Where(s => s.IsHittable).ToList();

            // 공격 가능한 적이 없으면 null 반환 (이동 필요)
            if (hittableScores.Count == 0)
            {
                Main.Log($"[TargetScore] No hittable targets! ({scores.Count} enemies exist but none attackable)");
                return null;
            }

            // 처치 가능한 적 우선
            if (preferKillable)
            {
                var killable = hittableScores.Where(s => s.KillableBonus > 0).OrderByDescending(s => s.TotalScore).FirstOrDefault();
                if (killable != null)
                {
                    Main.Log($"[TargetScore] Killable hittable target: {killable}");
                    return killable.Target;
                }
            }

            // 가까운 적 우선 (근접 무기용)
            if (preferClose)
            {
                var best = hittableScores.OrderByDescending(s => s.DistanceScore + s.HPPercentScore).FirstOrDefault();
                if (best != null)
                {
                    Main.Log($"[TargetScore] Close hittable target: {best}");
                    return best.Target;
                }
            }

            // 기본: 총점 기준
            var bestOverall = hittableScores.OrderByDescending(s => s.TotalScore).FirstOrDefault();
            if (bestOverall != null)
            {
                Main.Log($"[TargetScore] Best hittable target: {bestOverall}");
                return bestOverall.Target;
            }

            return null;  // 공격 가능한 적 없음
        }

        /// <summary>
        /// 모든 적에 대해 점수 계산
        /// ★ v2.2.19: 각 적에 대해 공격 가능 여부도 체크
        /// ★ v2.2.41: RangePreference 파라미터 추가 - 선호 무기로 공격 가능 여부 체크
        /// </summary>
        public static List<TargetScore> ScoreAllTargets(BaseUnitEntity unit, List<BaseUnitEntity> enemies, Settings.RangePreference rangePreference = Settings.RangePreference.Adaptive)
        {
            var scores = new List<TargetScore>();
            if (unit == null || enemies == null) return scores;

            // 거리 정규화용 최대값
            float maxDistance = 1f;
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.LifeState.IsDead) continue;
                float dist = GetDistance(unit, enemy);
                if (dist > maxDistance) maxDistance = dist;
            }

            // 예상 피해량 (기본 무기 기준 추정)
            float estimatedDamage = EstimateBaseDamage(unit);

            // ★ v2.2.41: RangePreference를 전달하여 선호 무기로 공격 가능 여부 체크
            AbilityData attackAbility = FindAnyAttackAbility(unit, rangePreference);

            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.LifeState.IsDead) continue;

                try
                {
                    var score = new TargetScore { Target = enemy };

                    float hpPercent = GetHPPercent(enemy);
                    int hpAbsolute = enemy.Health?.HitPointsLeft ?? 100;
                    float distance = GetDistance(unit, enemy);

                    // ★ v2.2.19: 공격 가능 여부 체크 (LOS, Range)
                    score.IsHittable = CheckIfHittable(unit, enemy, attackAbility);

                    // 1. HP% 점수: 낮을수록 높음 (0~100 -> 100~0)
                    score.HPPercentScore = (100f - hpPercent) / 100f * 30f;  // 최대 30점

                    // 2. 절대HP 점수: 낮을수록 높음 (게임 AI: 1/HP)
                    // 정규화: HP 1~500 -> 점수 20~0.04, cap at 20
                    score.HPAbsoluteScore = Math.Min(20f, 100f / Math.Max(5f, hpAbsolute));

                    // 3. 거리 점수: 가까울수록 높음
                    score.DistanceScore = (1f - distance / maxDistance) * 15f;  // 최대 15점

                    // 4. 처치 가능 보너스: 한 번에 죽일 수 있으면 큰 보너스
                    if (hpAbsolute <= estimatedDamage * 1.2f)  // 20% 여유
                    {
                        score.KillableBonus = 35f;  // 큰 보너스
                    }
                    else if (hpAbsolute <= estimatedDamage * 2f)
                    {
                        score.KillableBonus = 10f;  // 2타 내에 죽일 수 있음
                    }

                    // 총점 계산
                    score.TotalScore = score.HPPercentScore + score.HPAbsoluteScore +
                                      score.DistanceScore + score.KillableBonus;

                    // ★ v2.2.41: RangePreference adjustment 통합
                    float adjustment = 0f;
                    if (rangePreference == Settings.RangePreference.PreferRanged || rangePreference == Settings.RangePreference.MaintainRange)
                    {
                        if (distance > 5f)
                            adjustment = 20f;
                        else if (distance < 3f)
                            adjustment = -30f;

                        Main.LogDebug($"[TargetScore] {enemy.CharacterName}: RangedPref adjustment {adjustment:+0;-0;0} (dist={distance:F1}m)");
                    }
                    else if (rangePreference == Settings.RangePreference.PreferMelee)
                    {
                        adjustment = Math.Max(0f, (10f - distance) * 3f);
                        Main.LogDebug($"[TargetScore] {enemy.CharacterName}: MeleePref adjustment {adjustment:+0;-0;0} (dist={distance:F1}m)");
                    }
                    score.TotalScore += adjustment;

                    // ★ v2.2.19: 공격 불가능하면 점수 대폭 감소 (정렬용)
                    if (!score.IsHittable)
                    {
                        score.TotalScore -= 1000f;  // 공격 가능한 적보다 항상 낮게
                    }

                    scores.Add(score);
                }
                catch (Exception ex)
                {
                    Main.LogDebug($"[TargetScore] Error scoring {enemy?.CharacterName}: {ex.Message}");
                }
            }

            return scores;
        }

        /// <summary>
        /// ★ v2.2.37: RangePreference를 고려한 최적 타겟 찾기
        /// </summary>
        public static BaseUnitEntity FindBestTargetWithPreference(BaseUnitEntity unit, List<BaseUnitEntity> enemies, RangePreference rangePreference)
        {
            if (unit == null || enemies == null || enemies.Count == 0) return null;

            var scores = ScoreAllTargets(unit, enemies, rangePreference);
            if (scores.Count == 0) return null;

            // 공격 가능한 적만 필터링
            var hittableScores = scores.Where(s => s.IsHittable).ToList();
            if (hittableScores.Count == 0)
            {
                Main.Log($"[TargetScore] No hittable targets with {rangePreference}!");
                return null;
            }

            // 처치 가능한 적 우선
            var killable = hittableScores.Where(s => s.KillableBonus > 0).OrderByDescending(s => s.TotalScore).FirstOrDefault();
            if (killable != null)
            {
                Main.Log($"[TargetScore] ★ {rangePreference} killable: {killable}");
                return killable.Target;
            }

            // 총점 기준
            var bestOverall = hittableScores.OrderByDescending(s => s.TotalScore).FirstOrDefault();
            if (bestOverall != null)
            {
                Main.Log($"[TargetScore] ★ {rangePreference} best: {bestOverall}");
                return bestOverall.Target;
            }

            return null;
        }

        /// <summary>
        /// ★ v2.2.19: 유닛이 적을 공격할 수 있는지 확인 (LOS, Range)
        /// </summary>
        private static bool CheckIfHittable(BaseUnitEntity unit, BaseUnitEntity enemy, AbilityData attackAbility)
        {
            if (unit == null || enemy == null) return false;

            try
            {
                // 공격 능력이 있으면 그걸로 체크 (가장 정확한 방법)
                if (attackAbility != null)
                {
                    var targetWrapper = new TargetWrapper(enemy);
                    string reason;
                    bool canAttack = CanUseAbilityOn(attackAbility, targetWrapper, out reason);

                    if (!canAttack)
                    {
                        Main.LogDebug($"[Hittable] {unit.CharacterName} -> {enemy.CharacterName}: NO ({reason})");
                    }
                    return canAttack;
                }

                // 공격 능력이 없으면 거리만 체크 (폴백)
                // LOS는 게임이 자동으로 체크하므로 거리만 확인
                float distance = GetDistance(unit, enemy);
                bool inRange = distance <= 15f;  // 기본 무기 사거리 15m 가정

                if (!inRange)
                {
                    Main.LogDebug($"[Hittable] {unit.CharacterName} -> {enemy.CharacterName}: OUT OF RANGE ({distance:F1}m)");
                }

                return inRange;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[Hittable] Error: {ex.Message}");
                return true;  // 오류 시 가능하다고 가정
            }
        }

        /// <summary>
        /// ★ v2.2.19: 아무 공격 능력 찾기 (hittable 체크용)
        /// ★ v2.2.20: public으로 변경 - MoveAndCast fallback에서 사용
        /// ★ v2.2.40: RangePreference 파라미터 추가 (중앙화)
        /// </summary>
        public static AbilityData FindAnyAttackAbility(BaseUnitEntity unit, Settings.RangePreference rangePreference = Settings.RangePreference.Adaptive)
        {
            if (unit == null) return null;

            try
            {
                var abilities = unit.Abilities?.RawFacts;
                if (abilities == null) return null;

                // ★ v2.2.40: 선호하는 무기 타입 먼저 찾기
                AbilityData preferredAttack = null;
                AbilityData fallbackAttack = null;

                foreach (var ability in abilities)
                {
                    var abilityData = ability?.Data;
                    if (abilityData == null) continue;

                    // 무기 공격만
                    if (abilityData.Weapon == null) continue;
                    // 재장전 제외
                    if (AbilityDatabase.IsReload(abilityData)) continue;
                    // 수류탄 제외
                    if (CombatHelpers.IsGrenadeOrExplosive(abilityData)) continue;

                    List<string> reasons;
                    if (!IsAbilityAvailable(abilityData, out reasons)) continue;

                    // ★ v2.2.40: RangePreference에 맞는 무기 우선
                    if (CombatHelpers.IsPreferredWeaponType(abilityData, rangePreference))
                    {
                        preferredAttack = abilityData;
                        break;  // 선호 타입 발견 → 즉시 사용
                    }
                    else if (fallbackAttack == null)
                    {
                        fallbackAttack = abilityData;  // 폴백용 저장
                    }
                }

                // 선호 타입이 있으면 사용, 없으면 폴백
                if (preferredAttack != null)
                {
                    Main.LogDebug($"[GameAPI] FindAnyAttackAbility: Found preferred ({rangePreference}) attack: {preferredAttack.Name}");
                    return preferredAttack;
                }
                if (fallbackAttack != null)
                {
                    Main.LogDebug($"[GameAPI] FindAnyAttackAbility: No preferred, using fallback: {fallbackAttack.Name}");
                    return fallbackAttack;
                }

                // 무기 공격이 없으면 공격성 능력 찾기
                foreach (var ability in abilities)
                {
                    var abilityData = ability?.Data;
                    if (abilityData == null) continue;

                    if (IsOffensiveAbility(abilityData))
                    {
                        List<string> reasons;
                        if (IsAbilityAvailable(abilityData, out reasons))
                        {
                            return abilityData;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 유닛의 기본 피해량 추정
        /// 무기 API가 복잡하므로 간단한 휴리스틱 사용
        /// </summary>
        private static float EstimateBaseDamage(BaseUnitEntity unit)
        {
            if (unit == null) return 30f;  // 기본값

            try
            {
                // 휴리스틱: 레벨 기반 추정
                // 일반적으로 레벨 * 3 정도의 피해를 입힘
                int level = unit.Progression?.CharacterLevel ?? 10;
                float baseDamage = level * 3f + 10f;

                // 무기 장착 여부로 보정
                var primaryWeapon = unit.Body?.PrimaryHand?.MaybeWeapon;
                if (primaryWeapon != null)
                {
                    // 무기가 있으면 약간 보너스
                    baseDamage *= 1.2f;
                }

                return Math.Max(20f, baseDamage);
            }
            catch { }

            return 30f;  // 폴백 값
        }

        /// <summary>
        /// ★ 무기 타입에 맞는 최적 타겟 찾기
        /// </summary>
        public static BaseUnitEntity FindBestTargetForWeapon(BaseUnitEntity unit, List<BaseUnitEntity> enemies, bool isMelee)
        {
            if (isMelee)
            {
                // 근접: 가까운 적 우선, 처치 가능하면 더 좋음
                return FindBestTarget(unit, enemies, preferKillable: true, preferClose: true);
            }
            else
            {
                // 원거리: 처치 가능한 적 우선, 거리는 덜 중요
                return FindBestTarget(unit, enemies, preferKillable: true, preferClose: false);
            }
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

            var timing = AbilityDatabase.GetTiming(ability);
            var rule = AbilityDatabase.GetRule(ability);

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
                if (!AbilityDatabase.IsProactiveBuff(ability))
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
            return abilities.Where(a => AbilityDatabase.IsPostFirstAction(a)).ToList();
        }

        /// <summary>
        /// 턴 종료 스킬 필터링
        /// </summary>
        public static List<AbilityData> FilterTurnEndingAbilities(List<AbilityData> abilities)
        {
            return abilities.Where(a => AbilityDatabase.IsTurnEnding(a)).ToList();
        }

        /// <summary>
        /// 마무리 스킬 필터링
        /// </summary>
        public static List<AbilityData> FilterFinisherAbilities(List<AbilityData> abilities)
        {
            return abilities.Where(a => AbilityDatabase.IsFinisher(a)).ToList();
        }

        #endregion

        #region ★ v2.2.34: Retreat & Cover System

        /// <summary>
        /// ★ v2.2.43: 유닛의 적 목록 가져오기
        /// </summary>
        public static List<BaseUnitEntity> GetEnemies(BaseUnitEntity unit)
        {
            var enemies = new List<BaseUnitEntity>();
            if (unit == null) return enemies;

            try
            {
                var allUnits = Game.Instance?.State?.AllBaseAwakeUnits;
                if (allUnits == null) return enemies;

                foreach (var other in allUnits)
                {
                    if (other == null || other == unit) continue;
                    if (other.LifeState.IsDead) continue;
                    if (!other.IsPlayerEnemy && unit.Faction.IsPlayer) continue;
                    if (other.IsPlayerEnemy == unit.IsPlayerEnemy) continue;

                    enemies.Add(other);
                }
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] GetEnemies error: {ex.Message}");
            }

            return enemies;
        }

        /// <summary>
        /// 가장 가까운 적과의 거리 계산
        /// </summary>
        public static float GetNearestEnemyDistance(BaseUnitEntity unit, List<BaseUnitEntity> enemies)
        {
            if (unit == null || enemies == null || enemies.Count == 0)
                return float.MaxValue;

            float minDistance = float.MaxValue;
            foreach (var enemy in enemies)
            {
                if (enemy == null || enemy.LifeState.IsDead) continue;
                float dist = GetDistance(unit, enemy);
                if (dist < minDistance)
                    minDistance = dist;
            }

            return minDistance;
        }

        /// <summary>
        /// 위치의 Cover 타입 가져오기
        /// </summary>
        public static LosCalculations.CoverType GetCoverTypeAtPosition(Vector3 position, List<BaseUnitEntity> enemies)
        {
            if (enemies == null || enemies.Count == 0)
                return LosCalculations.CoverType.None;

            try
            {
                // 가장 좋은 Cover 찾기 (적들 기준)
                var bestCover = LosCalculations.CoverType.None;

                foreach (var enemy in enemies)
                {
                    if (enemy == null || enemy.LifeState.IsDead) continue;

                    // 적 -> 위치 방향으로 Cover 계산
                    var cover = LosCalculations.GetCoverType(position);
                    if (cover > bestCover)
                        bestCover = cover;
                }

                return bestCover;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] GetCoverTypeAtPosition error: {ex.Message}");
                return LosCalculations.CoverType.None;
            }
        }

        /// <summary>
        /// 후퇴 가능한 위치 찾기
        /// - 적과의 거리가 minDistance 이상
        /// - seekCover가 true면 Cover 있는 위치 우선
        /// ★ v2.2.34: 원거리 캐릭터 후퇴용
        /// </summary>
        public static Vector3? FindRetreatPosition(
            BaseUnitEntity unit,
            List<BaseUnitEntity> enemies,
            float minDistance,
            bool seekCover = true)
        {
            if (unit == null || enemies == null || enemies.Count == 0)
                return null;

            try
            {
                // 현재 위치
                Vector3 currentPos = unit.Position;

                // 적들의 중심점 계산
                Vector3 enemyCenter = Vector3.zero;
                int enemyCount = 0;
                foreach (var enemy in enemies)
                {
                    if (enemy == null || enemy.LifeState.IsDead) continue;
                    enemyCenter += enemy.Position;
                    enemyCount++;
                }
                if (enemyCount == 0) return null;
                enemyCenter /= enemyCount;

                // 적 중심에서 반대 방향으로 후퇴
                Vector3 retreatDirection = (currentPos - enemyCenter).normalized;
                if (retreatDirection == Vector3.zero)
                    retreatDirection = Vector3.back; // 기본 방향

                // 후보 위치들 생성 (부채꼴 형태로 탐색)
                var candidates = new List<(Vector3 pos, float score)>();

                // 이동 가능 거리 (AP 기반 추정)
                float moveRange = unit.CombatState?.ActionPointsBlue * 3f ?? 9f;
                moveRange = Mathf.Clamp(moveRange, 3f, 15f);

                // 여러 방향으로 탐색
                for (int angle = -60; angle <= 60; angle += 30)
                {
                    for (float dist = 3f; dist <= moveRange; dist += 3f)
                    {
                        // 회전된 방향
                        Vector3 rotatedDir = Quaternion.Euler(0, angle, 0) * retreatDirection;
                        Vector3 candidatePos = currentPos + rotatedDir * dist;

                        // 점수 계산
                        float score = 0f;

                        // 1. 적과의 최소 거리 (더 멀수록 좋음)
                        float nearestEnemyDist = float.MaxValue;
                        foreach (var enemy in enemies)
                        {
                            if (enemy == null || enemy.LifeState.IsDead) continue;
                            float d = Vector3.Distance(candidatePos, enemy.Position);
                            if (d < nearestEnemyDist) nearestEnemyDist = d;
                        }

                        // minDistance 미달이면 제외
                        if (nearestEnemyDist < minDistance) continue;

                        score += nearestEnemyDist * 2f; // 거리 점수

                        // 2. Cover 점수 (seekCover가 true일 때)
                        if (seekCover)
                        {
                            var coverType = GetCoverTypeAtPosition(candidatePos, enemies);
                            switch (coverType)
                            {
                                case LosCalculations.CoverType.Full:
                                    score += 30f;
                                    break;
                                case LosCalculations.CoverType.Half:
                                    score += 15f;
                                    break;
                                case LosCalculations.CoverType.Invisible:
                                    score += 50f; // 최고점
                                    break;
                            }
                        }

                        // 3. 이동 거리 패널티 (가까울수록 좋음)
                        score -= dist * 0.5f;

                        candidates.Add((candidatePos, score));
                    }
                }

                if (candidates.Count == 0)
                {
                    Main.Log($"[Retreat] No valid retreat position found for {unit.CharacterName}");
                    return null;
                }

                // 최고 점수 위치 선택
                var best = candidates.OrderByDescending(c => c.score).First();
                Main.Log($"[Retreat] {unit.CharacterName} retreating to {best.pos} (score={best.score:F1})");

                return best.pos;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[GameAPI] FindRetreatPosition error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 후퇴가 필요한지 확인
        /// - RangePreference가 PreferRanged 또는 MaintainRange
        /// - 가장 가까운 적이 MinSafeDistance 이내
        /// </summary>
        public static bool ShouldRetreat(BaseUnitEntity unit, List<BaseUnitEntity> enemies,
            Settings.RangePreference rangePreference, float minSafeDistance)
        {
            // 원거리 선호가 아니면 후퇴 불필요
            if (rangePreference != Settings.RangePreference.PreferRanged &&
                rangePreference != Settings.RangePreference.MaintainRange)
                return false;

            float nearestDist = GetNearestEnemyDistance(unit, enemies);
            return nearestDist <= minSafeDistance;
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
            var timing = AbilityDatabase.GetTiming(ability);
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
            if (AbilityDatabase.IsRighteousFury(ability))
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
            if (AbilityDatabase.IsTaunt(ability))
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
