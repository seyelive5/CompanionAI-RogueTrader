using System.Collections.Generic;
using Kingmaker.UnitLogic.Abilities;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.1: 스킬 사용 타이밍 분류 (확장됨)
    ///
    /// 핵심: 모든 스킬은 사용해야 할 "타이밍"이 있음
    /// - PreCombat: 전투 시작 전 (버프, 준비)
    /// - PreAttack: 공격 직전 (공격 강화 버프)
    /// - Attack: 공격 자체
    /// - PostFirstAction: 첫 행동 후 (Run and Gun, 추가 행동 활성화)
    /// - TurnEnding: 턴 마지막 (방어 자세, 쿨다운 절약)
    /// - Finisher: 적 HP 낮을 때만
    /// - Emergency: 위기 상황에서만 (HP 낮을 때)
    /// </summary>
    public enum AbilityTiming
    {
        /// <summary>일반 스킬 - 언제든 사용 가능</summary>
        Normal,

        /// <summary>선제적 자기 버프 - 턴 시작 시 우선 사용 (Defensive Stance 등)</summary>
        PreCombatBuff,

        /// <summary>공격 직전 버프 - 공격 전에 사용하면 효과적 (Concentrated Fire 등)</summary>
        PreAttackBuff,

        /// <summary>첫 행동 후 사용 - 추가 행동 활성화 (Run and Gun, 이동 후 공격 등)</summary>
        PostFirstAction,

        /// <summary>턴 종료 스킬 - 턴 마지막에만 사용 (Veil of Blades 등)</summary>
        TurnEnding,

        /// <summary>마무리 스킬 - 적 HP 낮을 때만 효과적 (Dispatch, Death Blow 등)</summary>
        Finisher,

        /// <summary>위기 스킬 - 자신/아군 HP 낮을 때만 (긴급 힐, 필사 스킬)</summary>
        Emergency,

        /// <summary>자해 스킬 - HP를 소모하므로 HP 체크 필요 (Blood Oath 등)</summary>
        SelfDamage,

        /// <summary>위험한 AoE - 아군 위치 확인 필수 (Lidless Stare 등)</summary>
        DangerousAoE,

        /// <summary>디버프 스킬 - 공격 전에 사용하면 효과적</summary>
        Debuff,

        /// <summary>지속 버프 - 한 번 사용 후 유지 (Stacking Buff)</summary>
        StackingBuff,

        // ★ v2.2.1 추가 타이밍

        /// <summary>Heroic Act - Momentum 175+ 필요</summary>
        HeroicAct,

        /// <summary>Desperate Measure - Momentum 50 이하일 때</summary>
        DesperateMeasure,

        /// <summary>모멘텀 생성 스킬 - Desperate 상태에서 우선 사용</summary>
        MomentumGeneration,

        /// <summary>Righteous Fury 계열 - 적 처치 후 버프</summary>
        RighteousFury,

        /// <summary>도발 스킬 - 근접 적 2명 이상일 때</summary>
        Taunt
    }

    /// <summary>
    /// 스킬 규칙 데이터
    /// </summary>
    public class AbilityRule
    {
        /// <summary>스킬 사용 타이밍</summary>
        public AbilityTiming Timing { get; set; }

        /// <summary>HP 임계값 (자해/필사 스킬용)</summary>
        public float HPThreshold { get; set; }

        /// <summary>전투당 1회만 사용?</summary>
        public bool SingleUsePerCombat { get; set; }

        /// <summary>설명</summary>
        public string Description { get; set; }

        /// <summary>적 HP 임계값 (마무리 스킬용, 0이면 무시)</summary>
        public float TargetHPThreshold { get; set; }

        public AbilityRule(AbilityTiming timing, float hpThreshold = 0f, bool singleUse = false, string desc = "")
        {
            Timing = timing;
            HPThreshold = hpThreshold;
            SingleUsePerCombat = singleUse;
            Description = desc;
            TargetHPThreshold = 0f;
        }

        public AbilityRule WithTargetHP(float threshold)
        {
            TargetHPThreshold = threshold;
            return this;
        }
    }

    /// <summary>
    /// 스킬 규칙 데이터베이스 - 게임 내 모든 주요 스킬의 사용 타이밍 정의
    /// </summary>
    public static class AbilityRulesDatabase
    {
        /// <summary>
        /// 블루프린트 이름 → 규칙 매핑
        /// 키는 소문자로 저장, 검색 시에도 소문자로 변환
        /// </summary>
        private static readonly Dictionary<string, AbilityRule> Rules = new()
        {
            // ========================================
            // ★ PostFirstAction - 첫 행동 후 사용해야 효과적인 스킬
            // ========================================

            // Run and Gun - 공격 후 이동 또는 이동 후 공격 가능
            // 사용 패턴: 공격 → Run and Gun → 이동 → 공격
            { "runandgun", new AbilityRule(AbilityTiming.PostFirstAction, 0f, false, "공격 후 사용하면 추가 행동 가능") },
            { "run_and_gun", new AbilityRule(AbilityTiming.PostFirstAction, 0f, false, "공격 후 사용하면 추가 행동 가능") },

            // Daring Breach - AP/MP 전체 회복 (이미 행동을 소모한 후 사용)
            { "daringbreach", new AbilityRule(AbilityTiming.PostFirstAction, 30f, false, "행동 소모 후 사용하면 추가 행동 가능") },
            { "daring_breach", new AbilityRule(AbilityTiming.PostFirstAction, 30f, false, "행동 소모 후 사용하면 추가 행동 가능") },

            // ========================================
            // 선제적 자기 버프 - 전투 시작 시 먼저 사용
            // ========================================

            // 방어 자세
            { "defensivestance", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "방어 자세") },
            { "defensive_stance", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "방어 자세") },
            { "fighterendure", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "인내 버프") },
            { "bulwark", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "원거리 방어") },
            { "unyieldingbeacon", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "피격 시 스택 버프") },
            { "braceforimpact", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "충격 대비") },
            { "brace_for_impact", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "충격 대비") },

            // 전방 배치 버프
            { "keystone_frontline", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "전방 구역 버프") },
            { "keystone_backline", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "보조 구역 버프") },
            { "keystone_rear", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "후방 구역 버프") },

            // ========================================
            // 공격 직전 버프 - 공격 바로 전에 사용
            // ========================================

            // 집중 사격 - 다음 원거리 공격 강화
            { "concentratedfire", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, false, "다음 원거리 공격 강화") },
            { "concentrated_fire", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, false, "다음 원거리 공격 강화") },

            // 지휘 스킬 - 아군 강화 후 공격
            { "voiceofcommand", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, true, "아군 스탯 버프") },
            { "voice_of_command", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, true, "아군 스탯 버프") },
            { "finesthour", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, true, "아군에게 추가 턴") },
            { "finest_hour", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, true, "아군에게 추가 턴") },
            { "bringitdown", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, true, "집중 공격 명령") },
            { "bring_it_down", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, true, "집중 공격 명령") },

            // 분석/표시 스킬
            { "analyseenemy", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, false, "적 분석") },
            { "analyse_enemy", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, false, "적 분석") },
            { "markprey", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, true, "타겟 마킹") },
            { "mark_prey", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, true, "타겟 마킹") },
            { "cullthebold", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, false, "대담한 자 처단") },

            // 돌진 스킬
            { "fightercharge", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, false, "돌진 후 공격") },
            { "fighter_charge", new AbilityRule(AbilityTiming.PreAttackBuff, 0f, false, "돌진 후 공격") },

            // ========================================
            // 턴 종료 스킬 - 마지막에만 사용
            // ========================================

            { "veilofblades", new AbilityRule(AbilityTiming.TurnEnding, 50f, false, "턴 종료됨, 방어용") },
            { "veil_of_blades", new AbilityRule(AbilityTiming.TurnEnding, 50f, false, "턴 종료됨, 방어용") },

            // ========================================
            // 마무리 스킬 - 적 HP 낮을 때만
            // ========================================

            { "dispatch", new AbilityRule(AbilityTiming.Finisher, 0f, false, "처형").WithTargetHP(30f) },
            { "deathblow", new AbilityRule(AbilityTiming.Finisher, 0f, false, "마무리 일격").WithTargetHP(25f) },
            { "death_blow", new AbilityRule(AbilityTiming.Finisher, 0f, false, "마무리 일격").WithTargetHP(25f) },
            { "execute", new AbilityRule(AbilityTiming.Finisher, 0f, false, "처형").WithTargetHP(30f) },

            // ========================================
            // 자해 스킬 - HP 체크 필요
            // ========================================

            { "bloodoath", new AbilityRule(AbilityTiming.SelfDamage, 60f, true, "HP 소모, 한 타겟에만") },
            { "blood_oath", new AbilityRule(AbilityTiming.SelfDamage, 60f, true, "HP 소모, 한 타겟에만") },
            { "reaperbloodoath", new AbilityRule(AbilityTiming.SelfDamage, 60f, true, "HP 소모") },
            { "oathofvengeance", new AbilityRule(AbilityTiming.SelfDamage, 60f, true, "복수 대상 지정") },
            { "ensanguinate", new AbilityRule(AbilityTiming.SelfDamage, 50f, false, "자해 후 버프") },
            { "recklessabandon", new AbilityRule(AbilityTiming.SelfDamage, 70f, false, "HP를 임시 HP로 전환") },
            { "metabolicovercharge", new AbilityRule(AbilityTiming.SelfDamage, 80f, true, "전투 종료까지 지속 피해") },

            // ========================================
            // 위험한 AoE - 아군 위치 확인 필수
            // ========================================

            { "lidlessstare", new AbilityRule(AbilityTiming.DangerousAoE, 0f, false, "160도 부채꼴 공격") },
            { "lidless_stare", new AbilityRule(AbilityTiming.DangerousAoE, 0f, false, "160도 부채꼴 공격") },
            { "bladedance", new AbilityRule(AbilityTiming.DangerousAoE, 0f, false, "주변 무작위 공격") },
            { "blade_dance", new AbilityRule(AbilityTiming.DangerousAoE, 0f, false, "주변 무작위 공격") },

            // ========================================
            // 디버프 스킬
            // ========================================

            { "exposeweakness", new AbilityRule(AbilityTiming.Debuff, 0f, false, "적 방어력 감소") },
            { "expose_weakness", new AbilityRule(AbilityTiming.Debuff, 0f, false, "적 방어력 감소") },
            { "dismantlingattack", new AbilityRule(AbilityTiming.Debuff, 0f, false, "약점 공략") },

            // ========================================
            // 지속/스택 버프
            // ========================================

            { "trenchline", new AbilityRule(AbilityTiming.StackingBuff, 0f, false, "참호선 전략") },
            { "listentoorder", new AbilityRule(AbilityTiming.StackingBuff, 0f, false, "지휘의 목소리") },

            // ========================================
            // ★ v2.2.1 추가: Righteous Fury 계열
            // ========================================

            { "revelinslaughter", new AbilityRule(AbilityTiming.RighteousFury, 0f, false, "적 3명 처치 후 활성화") },
            { "revel_in_slaughter", new AbilityRule(AbilityTiming.RighteousFury, 0f, false, "적 3명 처치 후 활성화") },
            { "holyrage", new AbilityRule(AbilityTiming.RighteousFury, 0f, false, "신성한 분노") },
            { "holy_rage", new AbilityRule(AbilityTiming.RighteousFury, 0f, false, "신성한 분노") },
            { "righteousfury", new AbilityRule(AbilityTiming.RighteousFury, 0f, false, "정의로운 분노") },
            { "righteous_fury", new AbilityRule(AbilityTiming.RighteousFury, 0f, false, "정의로운 분노") },

            // ========================================
            // ★ v2.2.1 추가: Heroic Act (Momentum 175+)
            // ========================================

            { "heroicact", new AbilityRule(AbilityTiming.HeroicAct, 0f, true, "Momentum 175+ 필요") },
            { "heroic_act", new AbilityRule(AbilityTiming.HeroicAct, 0f, true, "Momentum 175+ 필요") },
            { "heroicstrike", new AbilityRule(AbilityTiming.HeroicAct, 0f, true, "영웅적 일격") },
            { "heroic_strike", new AbilityRule(AbilityTiming.HeroicAct, 0f, true, "영웅적 일격") },

            // ========================================
            // ★ v2.2.1 추가: Desperate Measure (Momentum 낮을 때)
            // ========================================

            { "desperatemeasure", new AbilityRule(AbilityTiming.DesperateMeasure, 0f, true, "필사적 조치") },
            { "desperate_measure", new AbilityRule(AbilityTiming.DesperateMeasure, 0f, true, "필사적 조치") },
            { "laststand", new AbilityRule(AbilityTiming.DesperateMeasure, 0f, true, "최후의 저항") },
            { "last_stand", new AbilityRule(AbilityTiming.DesperateMeasure, 0f, true, "최후의 저항") },

            // ========================================
            // ★ v2.2.1 추가: 모멘텀 생성 스킬
            // ========================================

            { "warhymn", new AbilityRule(AbilityTiming.MomentumGeneration, 0f, false, "전쟁 찬가 - 모멘텀 생성") },
            { "war_hymn", new AbilityRule(AbilityTiming.MomentumGeneration, 0f, false, "전쟁 찬가 - 모멘텀 생성") },
            { "assignobjective", new AbilityRule(AbilityTiming.MomentumGeneration, 0f, false, "목표 지정 - 모멘텀 생성") },
            { "assign_objective", new AbilityRule(AbilityTiming.MomentumGeneration, 0f, false, "목표 지정 - 모멘텀 생성") },
            { "inspire", new AbilityRule(AbilityTiming.MomentumGeneration, 0f, false, "고무 - 조건부 모멘텀 회복") },

            // ========================================
            // ★ v2.2.1 추가: 추가 방어 자세
            // ========================================

            { "shieldwall", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "방패벽") },
            { "shield_wall", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "방패벽") },
            { "holdtheline", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "전선 사수") },
            { "hold_the_line", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "전선 사수") },
            { "holdline", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "전선 사수") },
            { "guardstance", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "수비 자세") },
            { "guard_stance", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "수비 자세") },
            { "fortify", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "방어 강화") },
            { "hunkerdown", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "엄폐") },
            { "hunker_down", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "엄폐") },
            { "entrench", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "참호 구축") },
            { "ironguard", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "철벽 수비") },
            { "iron_guard", new AbilityRule(AbilityTiming.PreCombatBuff, 0f, false, "철벽 수비") },

            // ========================================
            // ★ v2.2.1 추가: 도발 스킬
            // ========================================

            { "taunt", new AbilityRule(AbilityTiming.Taunt, 0f, false, "도발") },
            { "provoke", new AbilityRule(AbilityTiming.Taunt, 0f, false, "도발") },
            { "challengingroar", new AbilityRule(AbilityTiming.Taunt, 0f, false, "도전의 포효") },
            { "challenging_roar", new AbilityRule(AbilityTiming.Taunt, 0f, false, "도전의 포효") },
            { "drawfire", new AbilityRule(AbilityTiming.Taunt, 0f, false, "화력 유도") },
            { "draw_fire", new AbilityRule(AbilityTiming.Taunt, 0f, false, "화력 유도") },

            // ========================================
            // ★ v2.2.3 추가: 반응형 방어 스킬 (턴 마지막에 사용)
            // ========================================

            // 굳건한 방어 - 적 공격 시 AoE 반격
            { "stalwartdefense", new AbilityRule(AbilityTiming.TurnEnding, 0f, false, "반응형 방어 - 적 공격 시 반격") },
            { "stalwart_defense", new AbilityRule(AbilityTiming.TurnEnding, 0f, false, "반응형 방어 - 적 공격 시 반격") },
            { "shieldstalwartdefense", new AbilityRule(AbilityTiming.TurnEnding, 0f, false, "방패 반응형 방어") },
            { "shield_stalwart_defense", new AbilityRule(AbilityTiming.TurnEnding, 0f, false, "방패 반응형 방어") },

            // 방패 반격 스킬들
            { "shieldriposte", new AbilityRule(AbilityTiming.TurnEnding, 0f, false, "방패 반격") },
            { "shield_riposte", new AbilityRule(AbilityTiming.TurnEnding, 0f, false, "방패 반격") },
            { "shieldbash", new AbilityRule(AbilityTiming.Normal, 0f, false, "방패 강타") },
            { "shield_bash", new AbilityRule(AbilityTiming.Normal, 0f, false, "방패 강타") },

            // ========================================
            // ★ v2.2.3 추가: 갭 클로저/암살 스킬
            // ========================================

            // Death from Above - 점프 공격 (이동+공격 통합)
            { "deathfromabove", new AbilityRule(AbilityTiming.Normal, 0f, false, "점프 공격 - 갭 클로저") },
            { "death_from_above", new AbilityRule(AbilityTiming.Normal, 0f, false, "점프 공격 - 갭 클로저") },
            { "assassinstrike", new AbilityRule(AbilityTiming.Normal, 0f, false, "암살 일격") },
            { "assassin_strike", new AbilityRule(AbilityTiming.Normal, 0f, false, "암살 일격") },
            { "shadowstep", new AbilityRule(AbilityTiming.Normal, 0f, false, "그림자 이동") },
            { "shadow_step", new AbilityRule(AbilityTiming.Normal, 0f, false, "그림자 이동") },
            { "leapattack", new AbilityRule(AbilityTiming.Normal, 0f, false, "도약 공격") },
            { "leap_attack", new AbilityRule(AbilityTiming.Normal, 0f, false, "도약 공격") },
            { "pounce", new AbilityRule(AbilityTiming.Normal, 0f, false, "급습") },
        };

        /// <summary>
        /// 블루프린트 이름으로 스킬 규칙 조회
        /// </summary>
        public static AbilityRule GetRule(string blueprintName)
        {
            if (string.IsNullOrEmpty(blueprintName)) return null;

            // 소문자로 변환하고 공백/언더스코어 제거
            string key = blueprintName.ToLower()
                .Replace(" ", "")
                .Replace("_ability", "")
                .Replace("ability", "");

            // 정확한 키로 먼저 검색
            if (Rules.TryGetValue(key, out var rule))
                return rule;

            // 부분 매칭
            foreach (var kvp in Rules)
            {
                if (key.Contains(kvp.Key) || kvp.Key.Contains(key))
                    return kvp.Value;
            }

            return null;
        }

        /// <summary>
        /// AbilityData에서 스킬 규칙 조회
        /// </summary>
        public static AbilityRule GetRule(AbilityData ability)
        {
            if (ability == null) return null;

            // 블루프린트 이름으로 검색
            string bpName = ability.Blueprint?.name;
            if (!string.IsNullOrEmpty(bpName))
            {
                var rule = GetRule(bpName);
                if (rule != null) return rule;
            }

            // 능력 이름으로 검색
            string abilityName = ability.Name;
            if (!string.IsNullOrEmpty(abilityName))
            {
                return GetRule(abilityName);
            }

            return null;
        }

        /// <summary>
        /// 스킬의 타이밍 조회 (규칙 없으면 Normal 반환)
        /// </summary>
        public static AbilityTiming GetTiming(AbilityData ability)
        {
            var rule = GetRule(ability);
            return rule?.Timing ?? AbilityTiming.Normal;
        }

        /// <summary>
        /// 스킬이 특정 타이밍에 사용 가능한지 확인
        /// </summary>
        public static bool CanUseAtTiming(AbilityData ability, AbilityTiming currentTiming)
        {
            var timing = GetTiming(ability);

            // Normal 스킬은 언제든 가능
            if (timing == AbilityTiming.Normal) return true;

            // 정확한 타이밍 매칭
            return timing == currentTiming;
        }

        /// <summary>
        /// PostFirstAction 스킬인지 확인
        /// ★ v2.2.6: GUID 기반 우선
        /// </summary>
        public static bool IsPostFirstAction(AbilityData ability)
        {
            if (ability == null) return false;

            // GUID 기반 확인 (가장 정확)
            if (AbilityGuids.IsPostFirstActionAbility(ability))
                return true;

            return GetTiming(ability) == AbilityTiming.PostFirstAction;
        }

        /// <summary>
        /// 선제적 버프인지 확인 (PreCombatBuff 또는 PreAttackBuff)
        /// </summary>
        public static bool IsProactiveBuff(AbilityData ability)
        {
            var timing = GetTiming(ability);
            return timing == AbilityTiming.PreCombatBuff ||
                   timing == AbilityTiming.PreAttackBuff ||
                   timing == AbilityTiming.StackingBuff;
        }

        /// <summary>
        /// 턴 종료 스킬인지 확인
        /// </summary>
        public static bool IsTurnEnding(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.TurnEnding;
        }

        /// <summary>
        /// 마무리 스킬인지 확인
        /// ★ GUID 기반 우선
        /// </summary>
        public static bool IsFinisher(AbilityData ability)
        {
            if (ability == null) return false;
            if (AbilityGuids.IsFinisher(ability)) return true;
            return GetTiming(ability) == AbilityTiming.Finisher;
        }

        // ★ v2.2.1 추가 헬퍼 메서드

        /// <summary>
        /// Heroic Act 스킬인지 확인
        /// ★ GUID 기반 우선
        /// </summary>
        public static bool IsHeroicAct(AbilityData ability)
        {
            if (ability == null) return false;
            if (AbilityGuids.IsHeroicAct(ability)) return true;
            return GetTiming(ability) == AbilityTiming.HeroicAct;
        }

        /// <summary>
        /// Desperate Measure 스킬인지 확인
        /// </summary>
        public static bool IsDesperateMeasure(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.DesperateMeasure;
        }

        /// <summary>
        /// 모멘텀 생성 스킬인지 확인
        /// </summary>
        public static bool IsMomentumGeneration(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.MomentumGeneration;
        }

        /// <summary>
        /// Righteous Fury 스킬인지 확인
        /// </summary>
        public static bool IsRighteousFury(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.RighteousFury;
        }

        /// <summary>
        /// 도발 스킬인지 확인
        /// ★ GUID 기반 우선
        /// </summary>
        public static bool IsTaunt(AbilityData ability)
        {
            if (ability == null) return false;
            if (AbilityGuids.IsTaunt(ability)) return true;
            return GetTiming(ability) == AbilityTiming.Taunt;
        }

        /// <summary>
        /// 자해 스킬인지 확인
        /// </summary>
        public static bool IsSelfDamage(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.SelfDamage;
        }

        /// <summary>
        /// 위험한 AoE 스킬인지 확인
        /// </summary>
        public static bool IsDangerousAoE(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.DangerousAoE;
        }
    }
}
