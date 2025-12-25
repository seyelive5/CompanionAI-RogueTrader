using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;
using CompanionAI_v2_2.Core;
using CompanionAI_v2_2.Settings;
using static CompanionAI_v2_2.Core.AbilityDatabase;

namespace CompanionAI_v2_2.Strategies
{
    /// <summary>
    /// v2.2.0: 타이밍 인식 전략 기본 클래스
    ///
    /// 모든 역할 전략의 기반
    /// 공통 타이밍 로직과 안전성 체크 제공
    /// </summary>
    public abstract class TimingAwareStrategy : IUnitStrategy
    {
        public abstract string StrategyName { get; }

        // HP 소모 스킬 안전 임계값
        protected const float HP_COST_THRESHOLD = 40f;

        public abstract ActionDecision DecideAction(ActionContext ctx);

        #region Timing Phase Methods

        /// <summary>
        /// Phase 1: 긴급 자기 힐
        /// ★ v2.2.8: 메디킷 지원 개선 - CanTargetSelf 직접 확인
        /// </summary>
        protected ActionDecision TryEmergencySelfHeal(ActionContext ctx)
        {
            // 설정값 사용 (기본 50%, 긴급 시 30%)
            float threshold = ctx.Settings?.HealAtHPPercent ?? 50f;
            float emergencyThreshold = threshold * 0.6f; // 긴급 임계값 (설정의 60%)

            if (ctx.HPPercent >= emergencyThreshold) return null;

            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!IsHealAbility(ability)) continue;

                // ★ 메디킷 지원: CanTargetSelf 직접 확인 (Range가 Custom이어도 OK)
                try
                {
                    if (!ability.Blueprint.CanTargetSelf) continue;
                }
                catch { continue; }

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[{StrategyName}] Emergency heal: {ability.Name} (HP={ctx.HPPercent:F0}%)");
                    return ActionDecision.UseAbility(ability, target, "Emergency self-heal");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 1.5: 재장전
        /// ★ v2.2.32: "숲을 보는" 접근 - 게임의 판단을 신뢰
        ///
        /// 핵심: 우리가 탄약을 직접 체크하지 않음!
        /// 재장전 능력의 IsAvailable이 이미 탄약 체크를 포함함.
        /// 게임이 "재장전 가능"이라고 하면 재장전 필요.
        /// </summary>
        protected ActionDecision TryReload(ActionContext ctx)
        {
            // ★ 핵심: 재장전 능력이 "사용 가능"한지만 확인
            // 게임의 WeaponReloadLogic.IsAvailable()이 탄약 체크를 처리함
            // - 탄약 가득 → 재장전 능력 사용 불가 → null 반환
            // - 탄약 부족 → 재장전 능력 사용 가능 → 능력 반환
            var reloadAbility = GameAPI.FindAvailableReloadAbility(ctx.AvailableAbilities);

            if (reloadAbility == null)
            {
                // 재장전 능력이 없거나 사용 불가 (탄약 가득 포함)
                return null;
            }

            // 재장전 능력이 사용 가능 = 게임이 "재장전 필요"하다고 판단
            var target = new TargetWrapper(ctx.Unit);
            string reason;
            if (GameAPI.CanUseAbilityOn(reloadAbility, target, out reason))
            {
                Main.Log($"[{StrategyName}] ★ RELOAD: {reloadAbility.Name}");
                return ActionDecision.UseAbility(reloadAbility, target, "Reload");
            }
            else
            {
                Main.LogDebug($"[{StrategyName}] Reload ability found but blocked: {reason}");
            }

            return null;
        }

        /// <summary>
        /// Phase 2: 선제적 버프 (PreCombatBuff, PreAttackBuff)
        /// 첫 행동 전에만 사용
        /// ★ v2.2.10: AP 예약 시스템 - 무기 공격용 AP 보존
        /// ★ v2.2.10: 런앤건 명시적 차단 (공격 후에만 사용)
        /// </summary>
        protected ActionDecision TryProactiveBuffs(ActionContext ctx)
        {
            // 이미 첫 행동을 했으면 스킵
            if (ctx.HasPerformedFirstAction) return null;

            var proactiveBuffs = GameAPI.FilterProactiveBuffs(ctx.AvailableAbilities, ctx.Unit);
            if (proactiveBuffs.Count == 0) return null;

            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in proactiveBuffs)
            {
                // ★ v2.2.10: 런앤건은 첫 행동 전에 절대 사용하지 않음!
                if (AbilityDatabase.IsRunAndGun(ability))
                {
                    Main.LogDebug($"[{StrategyName}] Skip Run and Gun in proactive phase");
                    continue;
                }

                // PostFirstAction 스킬은 여기서 제외
                if (AbilityDatabase.IsPostFirstAction(ability)) continue;

                // ★ v2.2.10: AP 예약 체크 - 무기 공격용 AP 보존
                if (!GameAPI.CanAffordAbilityWithReserve(ctx, ability, ctx.ReservedAPForAttack))
                {
                    Main.LogDebug($"[{StrategyName}] Skip {ability.Name}: not enough AP (need {ctx.ReservedAPForAttack:F1} for attack)");
                    continue;
                }

                // 타이밍 체크
                if (!GameAPI.CanUseAbilityAtCurrentTiming(ability, ctx)) continue;

                // 사이킥 안전성
                if (!IsSafePsychicAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[{StrategyName}] Proactive buff: {ability.Name} (AP: {ctx.CurrentAP:F1} -> {ctx.CurrentAP - GameAPI.GetAbilityAPCost(ability):F1})");
                    return ActionDecision.UseAbility(ability, target, "Proactive buff");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 3: 디버프 적용 (첫 공격 전)
        /// ★ v2.2.10: AP 예약 체크 추가
        /// </summary>
        protected ActionDecision TryDebuffs(ActionContext ctx, BaseUnitEntity target)
        {
            if (target == null) return null;

            var targetWrapper = new TargetWrapper(target);

            foreach (var ability in ctx.AvailableAbilities)
            {
                var timing = AbilityDatabase.GetTiming(ability);
                if (timing != AbilityTiming.Debuff) continue;

                // 이미 적용된 디버프인지 체크
                if (GameAPI.HasActiveBuffOnTarget(target, ability)) continue;

                // ★ v2.2.10: AP 예약 체크 - 무기 공격용 AP 보존
                if (!GameAPI.CanAffordAbilityWithReserve(ctx, ability, ctx.ReservedAPForAttack))
                {
                    Main.LogDebug($"[{StrategyName}] Skip debuff {ability.Name}: not enough AP for attack");
                    continue;
                }

                if (!IsSafePsychicAbility(ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[{StrategyName}] Apply debuff: {ability.Name} -> {target.CharacterName} (AP: {ctx.CurrentAP:F1})");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Debuff on {target.CharacterName}");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 3.5: 특수 능력 (DoT 콤보, 연쇄 효과 등)
        /// ★ v2.2.29: SpecialAbilityHandler 통합
        /// </summary>
        protected ActionDecision TrySpecialAbilities(ActionContext ctx, BaseUnitEntity target)
        {
            if (target == null) return null;

            // 1. DoT 콤보 처리: DoT 강화 스킬 사용 전 DoT 적용
            var dotComboResult = TryDOTCombo(ctx, target);
            if (dotComboResult != null) return dotComboResult;

            // 2. 연쇄 효과 스킬 (다수 적 있을 때)
            var chainResult = TryChainEffect(ctx, target);
            if (chainResult != null) return chainResult;

            return null;
        }

        /// <summary>
        /// DoT 콤보 처리: DoT 강화 스킬 사용 조건 체크
        /// 예: Shape Flames 사용 전 Inferno로 Burning DoT 적용
        /// </summary>
        private ActionDecision TryDOTCombo(ActionContext ctx, BaseUnitEntity target)
        {
            // 사용 가능한 DoT 강화 스킬 찾기
            AbilityData dotIntensifyAbility = null;

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (SpecialAbilityHandler.IsDOTIntensifyAbility(ability))
                {
                    dotIntensifyAbility = ability;
                    break;
                }
            }

            if (dotIntensifyAbility == null) return null;

            // DoT 강화 스킬의 효과성 확인
            if (SpecialAbilityHandler.CanUseSpecialAbilityEffectively(dotIntensifyAbility, ctx, target))
            {
                // 타겟에 DoT 있음 → 바로 사용
                var targetWrapper = new TargetWrapper(target);
                string reason;
                if (GameAPI.CanUseAbilityOn(dotIntensifyAbility, targetWrapper, out reason))
                {
                    if (!IsSafePsychicAbility(dotIntensifyAbility)) return null;

                    Main.Log($"[{StrategyName}] ★ DoT Intensify: {dotIntensifyAbility.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(dotIntensifyAbility, targetWrapper, "DoT Intensify (target has DoT)");
                }
            }
            else
            {
                // 타겟에 DoT 없음 → 먼저 DoT 적용 스킬 사용
                foreach (var ability in ctx.AvailableAbilities)
                {
                    if (!SpecialAbilityHandler.AppliesBurningDOT(ability)) continue;

                    var targetWrapper = new TargetWrapper(target);
                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        if (!IsSafePsychicAbility(ability)) continue;

                        Main.Log($"[{StrategyName}] ★ DoT Setup: {ability.Name} -> {target.CharacterName} (for {dotIntensifyAbility.Name} combo)");
                        return ActionDecision.UseAbility(ability, targetWrapper, "Apply DoT for combo");
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 연쇄 효과 스킬 (다수 적 있을 때)
        /// </summary>
        private ActionDecision TryChainEffect(ActionContext ctx, BaseUnitEntity target)
        {
            // 적이 2명 이상일 때만
            if (ctx.Enemies.Count < 2) return null;

            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!SpecialAbilityHandler.IsChainEffectAbility(ability)) continue;

                // 연쇄 타겟 수 확인
                int chainTargets = SpecialAbilityHandler.CountChainTargets(ability, target, ctx.Enemies);
                if (chainTargets < 2)
                {
                    Main.LogDebug($"[{StrategyName}] Skip chain {ability.Name} - only {chainTargets} target(s)");
                    continue;
                }

                var targetWrapper = new TargetWrapper(target);
                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    if (!IsSafePsychicAbility(ability)) continue;

                    Main.Log($"[{StrategyName}] ★ Chain Effect: {ability.Name} -> {target.CharacterName} ({chainTargets} targets)");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Chain effect ({chainTargets} targets)");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 4: 공격 실행
        /// ★ v2.2.1: 상황에 맞는 스마트 공격 선택
        /// ★ v2.2.26: RangePreference 적용
        /// ★ v2.2.29: 특수 능력 효과성 체크 추가
        /// </summary>
        protected ActionDecision TryAttack(ActionContext ctx, BaseUnitEntity preferredTarget)
        {
            var target = preferredTarget ?? ctx.NearestEnemy;
            if (target == null) return null;

            // ★ v2.2.29: 특수 능력 우선 처리
            var specialResult = TrySpecialAbilities(ctx, target);
            if (specialResult != null) return specialResult;

            // 마무리 스킬 우선 (적 HP 낮은 경우)
            var finisherResult = TryFinisher(ctx, target);
            if (finisherResult != null) return finisherResult;

            // ★ v2.2.1: 스마트 공격 선택 - 상황에 맞는 최적 공격
            var attacks = GetOffensiveAbilities(ctx.AvailableAbilities);

            // ★ v2.2.26: RangePreference 적용
            var rangePreference = ctx.Settings?.RangePreference ?? RangePreference.Adaptive;

            // 우선순위별로 공격 정렬 (타겟 기준 + RangePreference 가중치)
            var prioritizedAttacks = attacks
                .Select(a => new {
                    Ability = a,
                    Priority = CombatHelpers.GetAttackPriority(a, ctx.Unit, target, ctx.Enemies, ctx.Allies),
                    Type = CombatHelpers.GetWeaponAttackType(a),
                    RangePenalty = GetRangePreferencePenalty(a, rangePreference)
                })
                .OrderBy(x => x.Priority + x.RangePenalty)
                .ToList();

            foreach (var attack in prioritizedAttacks)
            {
                var ability = attack.Ability;

                // ★ v2.2.29: 특수 능력 효과성 체크 (비효율적이면 스킵)
                if (SpecialAbilityHandler.IsSpecialAbility(ability))
                {
                    if (!SpecialAbilityHandler.CanUseSpecialAbilityEffectively(ability, ctx, target))
                    {
                        Main.LogDebug($"[{StrategyName}] Skip special ability {ability.Name} - not effective");
                        continue;
                    }
                }

                // 타이밍 체크
                if (!GameAPI.CanUseAbilityAtCurrentTiming(ability, ctx)) continue;

                // AoE 안전성
                if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, target, ctx.Allies)) continue;

                // 수류탄 효율성 체크 (적 2명 이상)
                if (!CombatHelpers.IsGrenadeEfficient(ability, target, ctx.Enemies))
                {
                    Main.LogDebug($"[{StrategyName}] Grenade {ability.Name} skipped - not enough enemies nearby");
                    continue;
                }

                // HP 소모 안전성
                if (!IsSafeHPCostAbility(ctx, ability)) continue;

                // 사이킥 안전성
                if (!IsSafePsychicAbility(ability)) continue;

                var targetWrapper = new TargetWrapper(target);
                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    string attackType = attack.Type.ToString();
                    Main.Log($"[{StrategyName}] Attack ({attackType}): {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Attack {target.CharacterName}");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 5: PostFirstAction 스킬 (Run and Gun 등)
        /// 첫 행동 후에만 사용
        /// </summary>
        protected ActionDecision TryPostFirstAction(ActionContext ctx)
        {
            if (!ctx.HasPerformedFirstAction) return null;

            var postActionAbilities = GameAPI.FilterPostFirstActionAbilities(ctx.AvailableAbilities);
            if (postActionAbilities.Count == 0) return null;

            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in postActionAbilities)
            {
                if (!GameAPI.CanUseAbilityAtCurrentTiming(ability, ctx)) continue;
                if (!IsSafeHPCostAbility(ctx, ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[{StrategyName}] ★ PostFirstAction SUCCESS: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Post-action ability (Run and Gun, etc.)");
                }
            }

            return null;
        }

        /// <summary>
        /// Phase 6: 턴 종료 스킬 (마지막에만)
        /// </summary>
        protected ActionDecision TryTurnEndingAbility(ActionContext ctx)
        {
            // 다른 유효한 공격이 있으면 스킵
            if (HasAnyValidAttack(ctx)) return null;

            var turnEndingAbilities = GameAPI.FilterTurnEndingAbilities(ctx.AvailableAbilities);
            if (turnEndingAbilities.Count == 0) return null;

            var target = new TargetWrapper(ctx.Unit);

            foreach (var ability in turnEndingAbilities)
            {
                // HP 소모 안전성
                if (!IsSafeHPCostAbility(ctx, ability)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    Main.Log($"[{StrategyName}] Turn ending: {ability.Name}");
                    return ActionDecision.UseAbility(ability, target, "Turn ending ability");
                }
            }

            return null;
        }

        /// <summary>
        /// 마무리 스킬 시도 (Finisher)
        /// </summary>
        protected ActionDecision TryFinisher(ActionContext ctx, BaseUnitEntity target)
        {
            if (target == null) return null;

            var finishers = GameAPI.FilterFinisherAbilities(ctx.AvailableAbilities);
            if (finishers.Count == 0) return null;

            foreach (var ability in finishers)
            {
                float threshold = AbilityDatabase.GetTargetHPThreshold(ability);

                // 타겟 HP 체크
                if (GameAPI.GetHPPercent(target) > threshold) continue;

                var targetWrapper = new TargetWrapper(target);
                string reason;
                if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                {
                    Main.Log($"[{StrategyName}] Finisher: {ability.Name} -> {target.CharacterName}");
                    return ActionDecision.UseAbility(ability, targetWrapper, $"Finish {target.CharacterName}");
                }
            }

            return null;
        }

        #endregion

        #region Safety Checks

        protected bool IsSafeHPCostAbility(ActionContext ctx, AbilityData ability)
        {
            if (!GameAPI.IsHPCostAbility(ability)) return true;

            float threshold = AbilityDatabase.GetHPThreshold(ability);
            if (threshold == 0f) threshold = HP_COST_THRESHOLD;

            if (ctx.HPPercent < threshold)
            {
                Main.LogDebug($"[{StrategyName}] HP cost {ability.Name} blocked - HP too low ({ctx.HPPercent:F0}% < {threshold}%)");
                return false;
            }

            return true;
        }

        protected bool IsSafePsychicAbility(AbilityData ability)
        {
            var safety = GameAPI.EvaluatePsychicSafety(ability);

            switch (safety)
            {
                case PsychicSafetyLevel.Safe:
                case PsychicSafetyLevel.Caution:
                    return true;

                case PsychicSafetyLevel.Dangerous:
                case PsychicSafetyLevel.Blocked:
                    Main.LogDebug($"[{StrategyName}] Psychic {ability.Name} blocked - {safety}");
                    return false;

                default:
                    return true;
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// 힐 스킬인지 확인
        /// ★ v2.2.7: GUID 기반 우선, 키워드 폴백
        /// </summary>
        protected bool IsHealAbility(AbilityData ability)
        {
            if (ability == null) return false;

            // GUID 기반 확인 우선
            if (AbilityDatabase.IsHealing(ability)) return true;

            // 키워드 폴백
            string name = ability.Name?.ToLower() ?? "";
            return name.Contains("heal") || name.Contains("mend") ||
                   name.Contains("medikit") || name.Contains("치유") || name.Contains("회복");
        }

        protected List<AbilityData> GetOffensiveAbilities(List<AbilityData> abilities)
        {
            return abilities
                .Where(a => {
                    // ★ v2.2.10: 무기 공격은 최우선 포함
                    bool isWeaponAttack = a.Weapon != null;

                    // 적 타겟 가능 체크 (무기 공격은 예외)
                    bool canTargetEnemy = a.Blueprint?.CanTargetEnemies == true || isWeaponAttack;
                    if (!canTargetEnemy) return false;

                    // 공격성 스킬 또는 무기 공격
                    bool isOffensive = isWeaponAttack ||
                                       GameAPI.IsOffensiveAbility(a) ||
                                       GameAPI.IsMeleeAbility(a) ||
                                       GameAPI.IsRangedAbility(a);
                    if (!isOffensive) return false;

                    // ★ v2.2.10: 특수 타이밍 스킬 제외 (디버프, 버프, Heroic Act 등)
                    // 무기 공격은 이 필터 적용 안함
                    if (!isWeaponAttack && AbilityDatabase.IsNonAttackTiming(a)) return false;

                    // 기존 필터
                    if (AbilityDatabase.IsPostFirstAction(a)) return false;
                    if (AbilityDatabase.IsTurnEnding(a)) return false;
                    if (AbilityDatabase.IsFinisher(a)) return false;

                    // 수류탄/재장전 제외 (무기 공격 중에서도)
                    if (isWeaponAttack && AbilityDatabase.IsReload(a)) return false;

                    return true;
                })
                .ToList();
        }

        /// <summary>
        /// 기본 무기 공격 필터링 (재장전, PostFirstAction, TurnEnding, 수류탄 제외)
        /// </summary>
        protected List<AbilityData> GetBasicWeaponAttacks(List<AbilityData> abilities)
        {
            return abilities
                .Where(a => a.Weapon != null &&
                           a.Blueprint?.CanTargetEnemies == true &&
                           !AbilityDatabase.IsPostFirstAction(a) &&
                           !AbilityDatabase.IsTurnEnding(a) &&
                           !CombatHelpers.IsGrenadeOrExplosive(a))
                .ToList();
        }

        /// <summary>
        /// ★ v2.2.26: RangePreference에 따른 무기 타입 페널티 계산
        /// 페널티가 낮을수록 우선순위가 높음
        /// WeaponAttackType: Melee=근접, Single/Burst/Scatter=원거리
        /// </summary>
        protected int GetRangePreferencePenalty(AbilityData ability, RangePreference preference)
        {
            if (ability == null) return 0;

            var weaponType = CombatHelpers.GetWeaponAttackType(ability);
            bool isMelee = weaponType == WeaponAttackType.Melee;
            // Single, Burst, Scatter = 원거리 공격
            bool isRanged = weaponType == WeaponAttackType.Single ||
                            weaponType == WeaponAttackType.Burst ||
                            weaponType == WeaponAttackType.Scatter;

            switch (preference)
            {
                case RangePreference.PreferMelee:
                    // 근접 선호: 원거리에 페널티
                    if (isRanged) return 100;
                    if (isMelee) return -50;  // 근접 보너스
                    return 0;

                case RangePreference.PreferRanged:
                    // 원거리 선호: 근접에 페널티
                    if (isMelee) return 100;
                    if (isRanged) return -50;  // 원거리 보너스
                    return 0;

                case RangePreference.MaintainRange:
                    // 거리 유지: 원거리 약간 선호 (근접 돌진 방지)
                    if (isMelee) return 50;
                    if (isRanged) return -25;
                    return 0;

                case RangePreference.Adaptive:
                default:
                    // 적응형: 페널티 없음 (기존 우선순위만 사용)
                    return 0;
            }
        }

        protected bool HasAnyValidAttack(ActionContext ctx)
        {
            var attacks = GetOffensiveAbilities(ctx.AvailableAbilities);

            foreach (var ability in attacks)
            {
                if (!IsSafeHPCostAbility(ctx, ability)) continue;
                if (!IsSafePsychicAbility(ability)) continue;

                foreach (var enemy in ctx.Enemies)
                {
                    if (enemy == null || enemy.LifeState.IsDead) continue;

                    if (!CombatHelpers.IsAoESafe(ability, ctx.Unit, enemy, ctx.Allies)) continue;

                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, new TargetWrapper(enemy), out reason))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// ★ v2.2.16: 이동 필요 시 게임 AI에 위임
        /// 우리 패치는 "직접 캐스트" 브랜치이므로 이동 명령 불가
        /// null 반환 시 게임의 "이동 후 공격" 로직이 처리
        /// </summary>
        protected ActionDecision TryMoveToEnemy(ActionContext ctx)
        {
            if (!ctx.CanMove) return null;
            if (ctx.NearestEnemy == null) return null;

            // 현재 위치에서 공격 가능한지 확인
            var attackResult = TryForceAttackIgnoringRange(ctx);
            if (attackResult != null) return attackResult;

            // ★ v2.2.16: 공격 불가 시 게임 AI에 위임
            // Move 대신 null 반환 → 패치가 Failure 반환 → 게임 AI가 이동+공격 처리
            Main.Log($"[{StrategyName}] Cannot attack from current position - delegating movement to game AI");
            return null;
        }

        /// <summary>
        /// Force Basic Attack 폴백 - 모든 스킬이 사용 불가할 때 기본 무기 공격
        /// </summary>
        protected ActionDecision TryForceBasicAttack(ActionContext ctx)
        {
            if (ctx.Enemies.Count == 0) return null;

            var basicAttacks = GetBasicWeaponAttacks(ctx.AvailableAbilities);
            if (basicAttacks.Count == 0) return null;

            var targets = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetDistance(ctx.Unit, e));

            foreach (var attack in basicAttacks)
            {
                foreach (var enemy in targets)
                {
                    var targetWrapper = new TargetWrapper(enemy);
                    string reason;
                    if (GameAPI.CanUseAbilityOn(attack, targetWrapper, out reason))
                    {
                        Main.Log($"[{StrategyName}] ★ FORCE BASIC ATTACK: {attack.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(attack, targetWrapper, $"Force basic attack on {enemy.CharacterName}");
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// ★ v2.2.16: 검증된 공격만 반환 (검증 실패한 공격 강제 불가)
        /// 이전 버전에서는 LoS/사거리 문제 있어도 공격을 강제했지만,
        /// 우리 패치는 "직접 캐스트" 브랜치라 이동 없이 실행됨 → 실패
        ///
        /// 검증 실패 시 null 반환 → 게임의 "이동 후 공격" 브랜치가 처리
        /// </summary>
        protected ActionDecision TryForceAttackIgnoringRange(ActionContext ctx)
        {
            if (ctx.Enemies.Count == 0) return null;

            var basicAttacks = GetBasicWeaponAttacks(ctx.AvailableAbilities);
            if (basicAttacks.Count == 0) return null;

            // 모든 적 시도 (가까운 순)
            var enemies = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetDistance(ctx.Unit, e))
                .ToList();

            foreach (var enemy in enemies)
            {
                var targetWrapper = new TargetWrapper(enemy);

                foreach (var attack in basicAttacks)
                {
                    string reason;
                    bool canUse = GameAPI.CanUseAbilityOn(attack, targetWrapper, out reason);

                    if (canUse)
                    {
                        // ★ v2.2.16: 검증 통과한 공격만 반환
                        Main.Log($"[{StrategyName}] Attack: {attack.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(attack, targetWrapper, $"Attack {enemy.CharacterName}");
                    }
                    else
                    {
                        // ★ v2.2.16: 검증 실패 시 로그만 남기고 다음 타겟 시도
                        Main.LogDebug($"[{StrategyName}] Cannot attack {enemy.CharacterName}: {reason}");
                    }
                }
            }

            // ★ v2.2.16: 모든 타겟이 공격 불가 → null 반환
            // 게임 AI의 "이동 후 공격" 로직이 처리하도록 위임
            Main.LogDebug($"[{StrategyName}] No valid attack target from current position - delegating to game AI");
            return null;
        }

        #endregion
    }
}
