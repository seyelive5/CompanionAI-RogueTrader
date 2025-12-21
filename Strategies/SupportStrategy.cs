using System.Collections.Generic;
using System.Linq;
using CompanionAI_v2.Core;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.Utility;

namespace CompanionAI_v2.Strategies
{
    /// <summary>
    /// v2.1.0: Support 전략 - 완전 재설계
    ///
    /// 핵심 원칙:
    /// 1. 게임 API(CanTarget)를 믿고 사용
    /// 2. 아군 타겟 능력과 적 타겟 능력을 명확히 분리
    /// 3. AoE 공격은 항상 아군 체크
    /// </summary>
    public class SupportStrategy : IUnitStrategy
    {
        public string StrategyName => "Support";

        // ★ v2.1.3: (능력ID + 타겟ID) 조합 추적 - 같은 버프를 다른 타겟에 사용 가능
        // 시간 기반 리셋 불필요 - 게임 상태가 바뀌면 CanUseAbilityOn이 다시 true 반환
        private static HashSet<string> _usedAbilityTargetPairs = new HashSet<string>();
        private static string _lastUnitId = null;

        public ActionDecision DecideAction(ActionContext ctx)
        {
            string unitId = ctx.Unit.UniqueId;

            // 유닛이 바뀌면 추적 초기화
            if (_lastUnitId != unitId)
            {
                _usedAbilityTargetPairs.Clear();
                _lastUnitId = unitId;
            }

            // ★ Veil 및 Momentum 상태 로깅
            int currentVeil = GameAPI.GetCurrentVeil();
            Main.Log($"[Support] {ctx.Unit.CharacterName}: HP={ctx.HPPercent:F0}%, {GameAPI.GetVeilStatusString()}, {GameAPI.GetMomentumStatusString()}, Allies={ctx.Allies.Count}, Enemies={ctx.Enemies.Count}");

            // 능력을 먼저 분류
            var allyTargetAbilities = new List<AbilityData>();
            var enemyTargetAbilities = new List<AbilityData>();

            ClassifyAbilities(ctx, allyTargetAbilities, enemyTargetAbilities);

            Main.LogDebug($"[Support] Classified: {allyTargetAbilities.Count} ally abilities, {enemyTargetAbilities.Count} enemy abilities");

            // 0. 모멘텀이 낮으면 모멘텀 생성 스킬 우선 (War Hymn, Assign Objective 등)
            if (GameAPI.IsDesperateMeasures())
            {
                var momentumResult = TryUseMomentumGeneratingAbility(ctx);
                if (momentumResult != null) return momentumResult;
            }

            // 1. 긴급 힐 (HP 낮은 아군)
            var healResult = TryHealAlly(ctx, allyTargetAbilities);
            if (healResult != null) return healResult;

            // 2. 아군 버프 (모든 아군 대상 능력 시도)
            var buffResult = TryBuffAlly(ctx, allyTargetAbilities);
            if (buffResult != null) return buffResult;

            // 3. 모멘텀 생성 스킬 (모멘텀이 낮지 않아도 사용 가능하면 사용)
            var momentumNormalResult = TryUseMomentumGeneratingAbility(ctx);
            if (momentumNormalResult != null) return momentumNormalResult;

            // 4. 안전한 공격 (AoE 체크 포함)
            var attackResult = TrySafeAttack(ctx, enemyTargetAbilities);
            if (attackResult != null) return attackResult;

            // 4. 위험하면 후퇴
            if (ctx.IsInMeleeRange && ctx.CanMove)
            {
                return ActionDecision.Move("Retreating from melee range");
            }

            return ActionDecision.EndTurn("No valid action");
        }

        /// <summary>
        /// 능력을 아군/적 타겟으로 분류 - 게임 API 사용
        /// </summary>
        private void ClassifyAbilities(ActionContext ctx,
            List<AbilityData> allyAbilities,
            List<AbilityData> enemyAbilities)
        {
            // 테스트 타겟
            var testAlly = ctx.Allies.FirstOrDefault() ?? ctx.Unit;
            var testEnemy = ctx.NearestEnemy;

            foreach (var ability in ctx.AvailableAbilities)
            {
                // ★ 무기 공격은 절대로 버프로 분류하면 안됨!
                // 근접/원거리 무기 공격은 아군을 타겟할 수 있지만 적에게 써야 함
                if (IsWeaponAttack(ability))
                {
                    enemyAbilities.Add(ability);
                    Main.LogDebug($"[Support] {ability.Name}: Weapon attack (always enemy)");
                    continue;
                }

                bool canTargetAlly = false;
                bool canTargetEnemy = false;

                // 자신 타겟 능력
                if (ability.Blueprint.Range == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityRange.Personal)
                {
                    allyAbilities.Add(ability);
                    Main.LogDebug($"[Support] {ability.Name}: Self-target ability");
                    continue;
                }

                // 아군 타겟 테스트
                if (testAlly != null)
                {
                    string reason;
                    canTargetAlly = GameAPI.CanUseAbilityOn(ability, new TargetWrapper(testAlly), out reason);
                }

                // 적 타겟 테스트
                if (testEnemy != null)
                {
                    string reason;
                    canTargetEnemy = GameAPI.CanUseAbilityOn(ability, new TargetWrapper(testEnemy), out reason);
                }

                if (canTargetAlly && !canTargetEnemy)
                {
                    allyAbilities.Add(ability);
                    Main.LogDebug($"[Support] {ability.Name}: Ally-only ability");
                }
                else if (canTargetEnemy)
                {
                    enemyAbilities.Add(ability);
                    Main.LogDebug($"[Support] {ability.Name}: Enemy-targetable ability");
                }
                else
                {
                    // 둘 다 안되면 일단 적 능력으로 (사거리 문제일 수 있음)
                    enemyAbilities.Add(ability);
                    Main.LogDebug($"[Support] {ability.Name}: Unknown target, treating as attack");
                }
            }
        }

        /// <summary>
        /// 무기 공격인지 확인 - 무기 공격은 절대 버프로 사용하면 안됨
        /// 단, 서포트 버프 능력은 제외해야 함
        /// ★ v2.1.2: 수류탄/폭발물도 공격으로 분류
        /// </summary>
        private bool IsWeaponAttack(AbilityData ability)
        {
            if (ability == null) return false;

            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            string name = ability.Name?.ToLower() ?? "";

            // ★ v2.1.2: 수류탄/폭발물은 절대 버프가 아님! (최우선 체크)
            if (IsGrenadeOrExplosive(ability, name, bpName))
            {
                Main.LogDebug($"[Support] GRENADE DETECTED: {ability.Name} - treating as attack");
                return true;  // 공격으로 처리
            }

            // ★ 원뿔형 공격 능력은 절대 버프가 아님! (최우선 체크)
            if (name.Contains("응시") || name.Contains("stare") ||
                bpName.Contains("lidless") || bpName.Contains("gaze") ||
                bpName.Contains("cone"))
            {
                return true;  // 공격으로 처리
            }

            // ★ 먼저 버프/서포트 능력인지 확인 - 버프는 무기 공격이 아님
            // Leader 계열 능력은 항상 버프
            if (bpName.Contains("leader")) return false;
            // Support 계열 능력은 항상 버프
            if (bpName.Contains("support")) return false;
            // 명시적 버프 키워드
            if (bpName.Contains("buff") || bpName.Contains("inspire") || bpName.Contains("command")) return false;
            // 한글 버프 키워드
            if (name.Contains("목소리") || name.Contains("드러내") || name.Contains("숴라") ||
                name.Contains("명령") || name.Contains("격려") || name.Contains("축복")) return false;
            // 영어 버프 키워드
            if (name.Contains("voice") || name.Contains("reveal") || name.Contains("inspire") ||
                name.Contains("command") || name.Contains("bless")) return false;

            // 아이템에서 온 능력은 무기 공격 가능성 높음
            bool isFromWeapon = ability.Weapon != null;
            if (!isFromWeapon)
            {
                // 무기가 아닌 능력은 추가 확인 필요
                // Personal 범위는 자기 버프
                if (ability.Blueprint.Range == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityRange.Personal)
                    return false;
            }

            // 블루프린트 이름으로 무기 공격 확인
            if (bpName.Contains("meleeattack") || bpName.Contains("melee_attack")) return true;
            if (bpName.Contains("singleshot") || bpName.Contains("single_shot")) return true;
            if (bpName.Contains("burstfire") || bpName.Contains("burst_fire")) return true;
            if (bpName.Contains("fullburst") || bpName.Contains("full_burst")) return true;
            if (bpName.Contains("attack_ability")) return true;
            if (bpName.Contains("swords") || bpName.Contains("pistol") || bpName.Contains("rifle")) return true;

            // 능력 이름으로 확인 (더 구체적으로)
            if (name.Contains("타격") && !name.Contains("목소리")) return true;
            if (name.Contains("사격") || name.Contains("난사") || name.Contains("발사")) return true;
            if (name.Contains("베기") || name.Contains("찌르기") || name.Contains("가르기")) return true;

            // 무기에서 온 능력이면 무기 공격
            if (isFromWeapon) return true;

            return false;
        }

        /// <summary>
        /// 모멘텀 생성 스킬 사용 시도 (War Hymn, Assign Objective 등)
        /// </summary>
        private ActionDecision TryUseMomentumGeneratingAbility(ActionContext ctx)
        {
            foreach (var ability in ctx.AvailableAbilities)
            {
                if (!GameAPI.IsMomentumGeneratingAbility(ability)) continue;

                // Veil 안전성 체크
                if (!IsSafeToUsePsychicAbility(ability)) continue;

                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;

                // 타겟 결정 (자신 또는 적 또는 아군)
                TargetWrapper target = null;
                string targetId = null;

                // 자신 타겟 스킬 (War Hymn)
                if (GameAPI.IsSelfTargetAbility(ability))
                {
                    target = new TargetWrapper(ctx.Unit);
                    targetId = ctx.Unit.UniqueId;
                }
                // 적 타겟 스킬 (Assign Objective)
                else if (GameAPI.IsOffensiveAbility(ability) && ctx.NearestEnemy != null)
                {
                    target = new TargetWrapper(ctx.NearestEnemy);
                    targetId = ctx.NearestEnemy.UniqueId;
                }
                // 아군 타겟 스킬 (Inspire, Strongpoint)
                else if (GameAPI.IsSupportAbility(ability))
                {
                    // 가장 HP 높은 아군 선택 (공격적으로 활용할 수 있는 아군)
                    var bestAlly = ctx.Allies
                        .Where(a => a != null && !a.LifeState.IsDead)
                        .OrderByDescending(a => GameAPI.GetHPPercent(a))
                        .FirstOrDefault();
                    if (bestAlly != null)
                    {
                        target = new TargetWrapper(bestAlly);
                        targetId = bestAlly.UniqueId;
                    }
                }

                if (target == null || targetId == null) continue;

                // ★ v2.1.3: (능력 + 타겟) 조합으로 중복 체크
                string pairKey = $"{abilityId}:{targetId}";
                if (_usedAbilityTargetPairs.Contains(pairKey)) continue;

                string reason;
                if (GameAPI.CanUseAbilityOn(ability, target, out reason))
                {
                    _usedAbilityTargetPairs.Add(pairKey);
                    string momentumStatus = GameAPI.IsDesperateMeasures() ? "DESPERATE - " : "";
                    Main.Log($"[Support] {momentumStatus}MOMENTUM BOOST: {ability.Name} - Current {GameAPI.GetMomentumStatusString()}");
                    return ActionDecision.UseAbility(ability, target, "Momentum generating ability");
                }
            }

            return null;
        }

        /// <summary>
        /// 아군 힐 시도
        /// </summary>
        private ActionDecision TryHealAlly(ActionContext ctx, List<AbilityData> allyAbilities)
        {
            // 가장 HP 낮은 대상 (자신 포함)
            var targets = new List<BaseUnitEntity> { ctx.Unit };
            targets.AddRange(ctx.Allies.Where(a => a != null && !a.LifeState.IsDead));

            var woundedTargets = targets
                .Where(t => GameAPI.GetHPPercent(t) < 70f)  // 70% 이하면 힐 고려
                .OrderBy(t => GameAPI.GetHPPercent(t))
                .ToList();

            if (woundedTargets.Count == 0) return null;

            // 힐 능력 찾기 (키워드 기반)
            var healAbilities = allyAbilities
                .Where(a => IsHealKeyword(a))
                .ToList();

            foreach (var target in woundedTargets)
            {
                var targetWrapper = new TargetWrapper(target);

                foreach (var ability in healAbilities)
                {
                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        Main.Log($"[Support] Heal: {ability.Name} -> {target.CharacterName} ({GameAPI.GetHPPercent(target):F0}%)");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Heal {target.CharacterName}");
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 아군 버프 시도 - 모든 아군 대상 능력을 아군에게 시도
        /// </summary>
        private ActionDecision TryBuffAlly(ActionContext ctx, List<AbilityData> allyAbilities)
        {
            // 버프 대상 (자신 포함)
            var targets = new List<BaseUnitEntity> { ctx.Unit };
            targets.AddRange(ctx.Allies.Where(a => a != null && !a.LifeState.IsDead));

            // 힐 제외한 아군 능력
            var buffAbilities = allyAbilities
                .Where(a => !IsHealKeyword(a))
                .ToList();

            Main.LogDebug($"[Support] TryBuffAlly: {buffAbilities.Count} buff abilities for {targets.Count} targets");

            foreach (var ability in buffAbilities)
            {
                // ★ Veil 안전성 체크 (사이킥 능력인 경우)
                if (!IsSafeToUsePsychicAbility(ability))
                {
                    continue;
                }

                string abilityId = ability.Blueprint?.AssetGuid?.ToString() ?? ability.Name;

                foreach (var target in targets)
                {
                    // ★ v2.1.3: (능력 + 타겟) 조합으로 중복 체크
                    string pairKey = $"{abilityId}:{target.UniqueId}";
                    if (_usedAbilityTargetPairs.Contains(pairKey))
                    {
                        Main.LogDebug($"[Support] Skipping already used: {ability.Name} -> {target.CharacterName}");
                        continue;
                    }

                    var targetWrapper = new TargetWrapper(target);

                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        // 사용한 조합 기록
                        _usedAbilityTargetPairs.Add(pairKey);
                        Main.Log($"[Support] Buff: {ability.Name} -> {target.CharacterName}");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Buff {target.CharacterName}");
                    }
                    else
                    {
                        Main.LogDebug($"[Support] Cannot buff {target.CharacterName} with {ability.Name}: {reason}");
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 안전한 공격 시도 - AoE 체크 강화
        /// </summary>
        private ActionDecision TrySafeAttack(ActionContext ctx, List<AbilityData> enemyAbilities)
        {
            if (ctx.Enemies.Count == 0) return null;

            // 적 목록 (가까운 순)
            var enemies = ctx.Enemies
                .Where(e => e != null && !e.LifeState.IsDead)
                .OrderBy(e => GameAPI.GetDistance(ctx.Unit, e))
                .ToList();

            // 근접 능력 제외, 안전한 것부터
            var safeAbilities = enemyAbilities
                .Where(a => !GameAPI.IsMeleeAbility(a))
                .OrderBy(a => IsAoEAbility(a) ? 1 : 0)  // 단일 타겟 우선
                .ToList();

            Main.LogDebug($"[Support] TrySafeAttack: {safeAbilities.Count} ranged abilities, {enemies.Count} enemies");

            foreach (var ability in safeAbilities)
            {
                // ★ Veil 안전성 체크 (사이킥 능력인 경우)
                if (!IsSafeToUsePsychicAbility(ability))
                {
                    continue;
                }

                bool isAoE = IsAoEAbility(ability);
                bool isCone = IsConeAbility(ability);

                foreach (var enemy in enemies)
                {
                    var targetWrapper = new TargetWrapper(enemy);

                    // 원뿔형 능력은 특별 체크 (160도 부채꼴)
                    if (isCone)
                    {
                        int alliesInCone = CountAlliesInCone(ctx, enemy.Position, 160f);
                        if (alliesInCone > 0)
                        {
                            Main.LogDebug($"[Support] CONE BLOCKED {ability.Name}: {alliesInCone} allies in cone toward {enemy.CharacterName}");
                            continue;  // 다음 적으로
                        }
                    }
                    // 일반 AoE는 반경 체크
                    else if (isAoE)
                    {
                        int alliesNearTarget = CountAlliesNearPosition(ctx, enemy.Position, 10f);
                        if (alliesNearTarget > 0)
                        {
                            Main.LogDebug($"[Support] BLOCKED {ability.Name}: {alliesNearTarget} allies near {enemy.CharacterName}");
                            continue;  // 다음 적으로
                        }
                    }

                    string reason;
                    if (GameAPI.CanUseAbilityOn(ability, targetWrapper, out reason))
                    {
                        Main.Log($"[Support] Attack: {ability.Name} -> {enemy.CharacterName}");
                        return ActionDecision.UseAbility(ability, targetWrapper, $"Attack {enemy.CharacterName}");
                    }
                    else
                    {
                        Main.LogDebug($"[Support] Cannot attack {enemy.CharacterName} with {ability.Name}: {reason}");
                    }
                }
            }

            return null;
        }

        #region Helpers

        /// <summary>
        /// 사이킥 능력을 안전하게 사용할 수 있는지 확인
        /// Veil 10-14: Major 사이킥 주의 (사용은 가능하지만 로그)
        /// Veil 15+: Major 사이킥 완전 차단
        /// </summary>
        private bool IsSafeToUsePsychicAbility(AbilityData ability)
        {
            var safetyLevel = GameAPI.EvaluatePsychicSafety(ability);

            switch (safetyLevel)
            {
                case PsychicSafetyLevel.Safe:
                    return true;

                case PsychicSafetyLevel.Caution:
                    // Veil 10-14에서 Major 사용: 경고 로그하고 허용
                    Main.Log($"[Support] CAUTION: Using Major psychic {ability.Name} at {GameAPI.GetVeilStatusString()}");
                    return true;

                case PsychicSafetyLevel.Dangerous:
                    // 사용 후 Veil 15+ 도달: 차단
                    Main.Log($"[Support] BLOCKED: {ability.Name} would push Veil to DANGER zone ({GameAPI.GetCurrentVeil()}+{GameAPI.GetVeilIncrease(ability)}>=15)");
                    return false;

                case PsychicSafetyLevel.Blocked:
                    // 이미 Veil 15+ 상태에서 Major: 완전 차단
                    Main.Log($"[Support] BLOCKED: {ability.Name} - Veil already at DANGER level ({GameAPI.GetVeilStatusString()})");
                    return false;

                default:
                    return true;
            }
        }

        private bool IsHealKeyword(AbilityData ability)
        {
            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            return name.Contains("heal") || name.Contains("cure") ||
                   name.Contains("restore") || name.Contains("치유") ||
                   name.Contains("회복") || name.Contains("mend") ||
                   bpName.Contains("heal") || bpName.Contains("cure") ||
                   bpName.Contains("medikit");
        }

        private bool IsAoEAbility(AbilityData ability)
        {
            if (ability == null) return false;

            // 게임 API
            if (ability.IsAOE) return true;
            if (ability.GetPatternSettings() != null) return true;

            // 이름 기반 (백업)
            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            return name.Contains("응시") || name.Contains("stare") ||
                   name.Contains("burst") || name.Contains("explod") ||
                   name.Contains("area") || name.Contains("cone") ||
                   name.Contains("line") || name.Contains("wave") ||
                   bpName.Contains("aoe") || bpName.Contains("area") ||
                   bpName.Contains("lidless");  // Lidless Stare 특별 처리
        }

        /// <summary>
        /// 원뿔형 AoE인지 확인 (160도 부채꼴)
        /// </summary>
        private bool IsConeAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // 원뿔형 능력 키워드
            return name.Contains("응시") || name.Contains("stare") ||
                   name.Contains("cone") || name.Contains("breath") ||
                   bpName.Contains("lidless") || bpName.Contains("cone") ||
                   bpName.Contains("gaze");
        }

        /// <summary>
        /// 특정 위치 근처의 아군 수 계산
        /// </summary>
        private int CountAlliesNearPosition(ActionContext ctx, UnityEngine.Vector3 position, float radius)
        {
            int count = 0;
            foreach (var ally in ctx.Allies)
            {
                if (ally == null || ally.LifeState.IsDead) continue;

                float distance = UnityEngine.Vector3.Distance(ally.Position, position);
                if (distance <= radius)
                {
                    count++;
                    Main.LogDebug($"[Support] Ally {ally.CharacterName} is {distance:F1}m from target position");
                }
            }

            // 자신도 체크 (캐스터 자신이 범위에 있을 수 있음)
            float selfDistance = UnityEngine.Vector3.Distance(ctx.Unit.Position, position);
            if (selfDistance <= radius && selfDistance > 0.1f)  // 자기 자신 위치 제외
            {
                count++;
                Main.LogDebug($"[Support] Self is {selfDistance:F1}m from target position");
            }

            return count;
        }

        /// <summary>
        /// 원뿔 범위 내의 아군 수 계산 (160도 부채꼴)
        /// </summary>
        private int CountAlliesInCone(ActionContext ctx, UnityEngine.Vector3 targetPosition, float coneAngle)
        {
            int count = 0;
            var casterPos = ctx.Unit.Position;
            var toTarget = targetPosition - casterPos;
            float targetDistance = toTarget.magnitude;

            // 타겟까지의 방향 벡터 (정규화)
            var targetDirection = toTarget.normalized;

            // 콘 범위 체크할 최대 거리 (타겟 거리 + 여유분)
            float maxCheckDistance = targetDistance + 5f;

            foreach (var ally in ctx.Allies)
            {
                if (ally == null || ally.LifeState.IsDead) continue;

                var toAlly = ally.Position - casterPos;
                float allyDistance = toAlly.magnitude;

                // 너무 가까운 아군은 무시 (시전자 바로 옆)
                if (allyDistance < 1f) continue;

                // 너무 먼 아군은 무시
                if (allyDistance > maxCheckDistance) continue;

                // 아군 방향과 타겟 방향 사이의 각도 계산
                var allyDirection = toAlly.normalized;
                float dotProduct = UnityEngine.Vector3.Dot(targetDirection, allyDirection);
                float angle = UnityEngine.Mathf.Acos(UnityEngine.Mathf.Clamp(dotProduct, -1f, 1f)) * UnityEngine.Mathf.Rad2Deg;

                // 원뿔 각도의 절반 내에 있으면 (160도면 각 방향 80도)
                float halfConeAngle = coneAngle / 2f;
                if (angle <= halfConeAngle)
                {
                    count++;
                    Main.LogDebug($"[Support] CONE: Ally {ally.CharacterName} at angle {angle:F1}° (within {halfConeAngle}°), dist={allyDistance:F1}m");
                }
            }

            return count;
        }

        /// <summary>
        /// ★ v2.1.2: 수류탄/폭발물인지 확인
        /// 수류탄은 아군에게 "던질 수 있지만" 버프가 아님!
        /// 자신이나 아군을 타겟으로 선택되면 자폭/아군 공격이 됨
        /// </summary>
        private bool IsGrenadeOrExplosive(AbilityData ability, string name, string bpName)
        {
            // 수류탄 키워드 (한글)
            if (name.Contains("수류탄") || name.Contains("폭탄") ||
                name.Contains("화염") || name.Contains("섬광") ||
                name.Contains("연막") || name.Contains("독가스") ||
                name.Contains("krak") || name.Contains("frag"))
                return true;

            // 수류탄 키워드 (영어)
            if (name.Contains("grenade") || name.Contains("bomb") ||
                name.Contains("explosive") || name.Contains("molotov") ||
                name.Contains("incendiary") || name.Contains("flashbang"))
                return true;

            // 블루프린트 이름 체크
            if (bpName.Contains("grenade") || bpName.Contains("bomb") ||
                bpName.Contains("explosive") || bpName.Contains("frag") ||
                bpName.Contains("krak") || bpName.Contains("incendiary") ||
                bpName.Contains("throwable") || bpName.Contains("thrown"))
                return true;

            // CanTargetPoint인 아이템 능력은 높은 확률로 투척물
            // (무기가 아니면서 위치를 타겟으로 할 수 있는 능력)
            if (ability.Weapon == null &&
                ability.Blueprint != null &&
                ability.Blueprint.CanTargetPoint)
            {
                // 명시적 버프 키워드가 없으면 투척물로 간주
                bool isNotBuff = !name.Contains("heal") && !name.Contains("치유") &&
                                 !name.Contains("buff") && !name.Contains("강화") &&
                                 !name.Contains("bless") && !name.Contains("축복") &&
                                 !name.Contains("protect") && !name.Contains("보호");

                if (isNotBuff)
                {
                    Main.LogDebug($"[Support] Suspected throwable (CanTargetPoint + no buff keywords): {ability.Name}");
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}
