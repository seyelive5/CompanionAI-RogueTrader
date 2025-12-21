using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Kingmaker;
using Kingmaker.AI;
using Kingmaker.AI.AreaScanning;
using Kingmaker.AI.BehaviourTrees;
using Kingmaker.AI.BehaviourTrees.Nodes;
using Kingmaker.AI.Learning;
using Kingmaker.AI.TargetSelectors;
using Kingmaker.Pathfinding;
using Pathfinding;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.Utility;
using Kingmaker.View.Covers;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules;
using UnityEngine;

namespace CompanionAI_v2.Patches
{
    /// <summary>
    /// 고도화된 커스텀 AI - 역할 기반 의사결정 시스템
    /// </summary>
    public static class CustomAIPatches
    {
        #region Enums & Constants

        /// <summary>
        /// 유닛 역할 (Settings.AIRole과 매핑)
        /// </summary>
        public enum UnitRole
        {
            Balanced,   // 균형 - 상황 적응
            Tank,       // 탱커 - 방어/어그로
            DPS,        // 딜러 - 데미지 최대화
            Support,    // 서포터 - 버프/디버프
            Hybrid,     // 하이브리드 - 근접/원거리 겸용
            Sniper      // 스나이퍼 - 원거리 유지
        }

        /// <summary>
        /// 게임 내 아키타입 (자동 감지용)
        /// </summary>
        public enum GameArchetype
        {
            Unknown,
            // Base Archetypes
            Warrior,        // 근접 탱커/딜러
            Officer,        // 지원/버퍼
            Operative,      // 약점 공략, 원거리
            Soldier,        // 원거리 딜러
            Bladedancer,    // 근접 회피형
            // Advanced Archetypes
            Vanguard,       // 프론트라인 탱커 (Warrior+Officer)
            Assassin,       // 버스트 딜러
            BountyHunter,   // 크리티컬 특화
            MasterTactician,// 모멘텀 딜러
            GrandStrategist,// 전장 통제
            ArchMilitant,   // 근접/원거리 겸용
            Executioner,    // DoT 딜러
            Overseer,       // 소환수 지원
            // Special
            Psyker,         // 사이커
            TechPriest      // 테크프리스트
        }

        /// <summary>
        /// 어빌리티 카테고리
        /// </summary>
        public enum AbilityCategory
        {
            Attack,         // 공격
            Heal,           // 치유
            Buff,           // 버프
            Debuff,         // 디버프
            Movement,       // 이동
            Defense,        // 방어
            Taunt,          // 도발 (탱커 전용)
            Exploit,        // 약점 공략 (Operative)
            Utility,        // 유틸리티
            Unknown
        }

        /// <summary>
        /// 전투 단계
        /// </summary>
        public enum CombatPhase
        {
            Opening,    // 전투 시작 (버프 시간)
            Mid,        // 중반 (공격/힐 밸런스)
            Cleanup     // 마무리 (약한 적 처리)
        }

        // 점수 가중치 상수
        private const float SCORE_BASE = 100f;
        private const float SCORE_FINISH_BONUS = 80f;
        private const float SCORE_FOCUS_FIRE_BONUS = 40f;
        private const float SCORE_HEAL_URGENT = 150f;
        private const float SCORE_HEAL_NORMAL = 50f;
        private const float SCORE_BUFF_OPENING = 60f;
        private const float SCORE_BUFF_NORMAL = 20f;
        private const float SCORE_DISTANCE_PENALTY = 2f;
        private const float SCORE_AP_EFFICIENCY = 10f;
        private const float SCORE_TAUNT_BONUS = 100f;       // 도발 스킬 보너스
        private const float SCORE_EXPLOIT_BONUS = 50f;      // 약점 공략 보너스

        // 명중률 관련 상수
        private const float MIN_ACCEPTABLE_HIT_CHANCE = 60f;  // 최소 허용 명중률 (%) - 60% 이하면 이동 고려
        private const float LOW_HIT_CHANCE_PENALTY = 100f;    // 낮은 명중률 페널티 (더 강한 페널티)
        private const float COVER_FULL_PENALTY = 40f;         // 완전 엄폐 명중률 감소
        private const float COVER_HALF_PENALTY = 20f;         // 부분 엄폐 명중률 감소
        private const float RANGE_PENALTY_PER_TILE = 2f;      // 타일당 거리 명중률 감소

        // 아키타입별 어빌리티 키워드
        private static readonly Dictionary<GameArchetype, string[]> ArchetypeAbilityKeywords = new()
        {
            { GameArchetype.Vanguard, new[] { "vanguard", "bulwark", "provocation", "unyielding", "beacon", "deflect" } },
            { GameArchetype.Warrior, new[] { "fighter", "warrior", "taunt", "endure", "reckless", "defensive stance" } },
            { GameArchetype.Officer, new[] { "officer", "command", "inspire", "order", "rally", "voice of" } },
            { GameArchetype.Operative, new[] { "operative", "analyse", "exploit", "dismantle", "weakness" } },
            { GameArchetype.Soldier, new[] { "soldier", "firearm", "run and gun", "dash", "burst", "suppressing" } },
            { GameArchetype.Bladedancer, new[] { "bladedancer", "blade", "dance", "parry", "riposte", "evasion" } },
            { GameArchetype.Assassin, new[] { "assassin", "death blow", "execute", "lethal", "backstab" } },
            { GameArchetype.BountyHunter, new[] { "bounty", "hunter", "mark", "prey", "critical" } },
            { GameArchetype.MasterTactician, new[] { "tactician", "momentum", "tactical" } },
            { GameArchetype.GrandStrategist, new[] { "strategist", "strategy", "control" } },
            { GameArchetype.ArchMilitant, new[] { "arch-militant", "versatility", "kick", "devastating" } },
            { GameArchetype.Psyker, new[] { "psyker", "psychic", "warp", "ignite", "shriek", "telepathy", "biomancy" } },
            { GameArchetype.TechPriest, new[] { "mechadendrite", "servo", "tech", "binary", "omnissiah" } }
        };

        #endregion

        #region Ability-Specific Rules (위키 기반)

        /// <summary>
        /// 스킬별 특수 규칙 타입
        /// </summary>
        public enum AbilityRuleType
        {
            Normal,             // 일반 스킬
            SelfDamage,         // 자해 스킬 (HP 체크 필요)
            SingleTargetBuff,   // 단일 타겟 버프 (한 번만 사용)
            PreAttackBuff,      // 공격 전 버프 (Opening에서 사용)
            TurnEnding,         // 턴 종료 스킬 (마지막에만 사용)
            Finisher,           // 마무리 스킬 (낮은 HP 적에게)
            StackingBuff,       // 중첩 버프 (계속 유지)
            Desperate,          // 필사 스킬 (HP 낮을 때만)
            Debuff,             // 디버프 스킬
            DangerousAoE,       // 위험한 광역 스킬 - 아군이 1명이라도 있으면 절대 금지
            RandomMeleeAoE      // 무작위 근접 공격 스킬 - 근처 무작위 대상 공격 (Blade Dance 등)
        }

        /// <summary>
        /// 스킬별 규칙 정의 (블루프린트 이름 기반)
        /// </summary>
        private static readonly Dictionary<string, AbilityRuleData> AbilityRules = new()
        {
            // === Bladedancer/Reaper 자해 스킬 ===
            // Blood Oath (피의 맹세) - 다음 공격이 회피/패리 불가, 킬 시 HP 회복
            // Blueprint: ReaperBloodOath_Ability
            { "bloodoath", new AbilityRuleData(AbilityRuleType.SelfDamage, 60f, true, "한 타겟에만 사용 후 즉시 공격") },
            { "reaperbloodoath", new AbilityRuleData(AbilityRuleType.SelfDamage, 60f, true, "한 타겟에만 사용 후 즉시 공격") },
            { "oathofvengeance", new AbilityRuleData(AbilityRuleType.SelfDamage, 60f, true, "복수 대상 지정") },
            { "veilofblades", new AbilityRuleData(AbilityRuleType.TurnEnding, 50f, false, "턴 종료됨, 방어용") },
            { "deathfromabove", new AbilityRuleData(AbilityRuleType.Normal, 40f, false, "점프 공격") },

            // === Executioner 자해 스킬 ===
            // Blueprint: Executioner_Ensanguinate_Ability (피 흘리기)
            { "ensanguinate", new AbilityRuleData(AbilityRuleType.SelfDamage, 50f, false, "자해 후 버프") },
            { "recklessabandon", new AbilityRuleData(AbilityRuleType.SelfDamage, 70f, false, "30% HP를 임시 HP로 전환") },

            // === Psyker 자해 스킬 ===
            { "metabolicovercharge", new AbilityRuleData(AbilityRuleType.SelfDamage, 80f, true, "전투 종료까지 지속 피해") },

            // === Warrior 스킬 ===
            { "daringbreach", new AbilityRuleData(AbilityRuleType.Desperate, 30f, false, "AP/MP 전체 회복, 필사") },
            { "fightercharge", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "돌진 후 공격") },
            { "forcefulstrike", new AbilityRuleData(AbilityRuleType.Normal, 0f, false, "밀어내기 공격") },

            // === Officer 버프 스킬 ===
            { "voiceofcommand", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, true, "아군 스탯 버프") },
            { "finesthour", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, true, "아군에게 추가 턴") },
            { "bringitdown", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, true, "아군에게 추가 턴(제한적)") },

            // === Soldier 버프 스킬 ===
            { "concentratedfire", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "다음 원거리 공격 강화") },
            { "runandgun", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "이동 후 공격") },
            { "firearmsmastery", new AbilityRuleData(AbilityRuleType.Normal, 0f, false, "추가 공격") },

            // === Operative 스킬 ===
            { "dismantlingattack", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "약점 공략 후 공격") },
            { "exposeweakness", new AbilityRuleData(AbilityRuleType.Debuff, 0f, false, "적 방어력 감소") },
            { "analyseenemy", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "적 분석") },

            // === Vanguard 스킬 ===
            { "unyieldingbeacon", new AbilityRuleData(AbilityRuleType.StackingBuff, 0f, false, "피격 시 스택 증가") },
            { "bulwark", new AbilityRuleData(AbilityRuleType.StackingBuff, 0f, false, "원거리 방어") },
            { "provocation", new AbilityRuleData(AbilityRuleType.Normal, 0f, false, "도발") },

            // === Assassin 스킬 ===
            { "dispatch", new AbilityRuleData(AbilityRuleType.Finisher, 0f, false, "처형 (잃은 HP% 추가 피해)") },
            { "dansemacabre", new AbilityRuleData(AbilityRuleType.Normal, 0f, false, "회피 증가 돌진") },
            { "deathblow", new AbilityRuleData(AbilityRuleType.Finisher, 0f, false, "마무리 일격") },

            // === Bounty Hunter 스킬 ===
            { "cullthebold", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "다음 공격 강화, 방어구 영구 감소") },
            { "markprey", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, true, "타겟 마킹") },

            // === Grand Strategist 스킬 ===
            // Strategist_Keystone_Frontline_Ability, Strategist_Keystone_Backline_Ability, Strategist_Keystone_Rear_Ability
            { "keystone_frontline", new AbilityRuleData(AbilityRuleType.StackingBuff, 0f, false, "전방 구역 - 아군 버프") },
            { "keystone_backline", new AbilityRuleData(AbilityRuleType.StackingBuff, 0f, false, "보조 구역 - 아군 버프") },
            { "keystone_rear", new AbilityRuleData(AbilityRuleType.StackingBuff, 0f, false, "후방 구역 - 아군 버프") },
            { "trenchline", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "참호선 전략") },

            // === Leader/Officer 스킬 ===
            { "listentoorder", new AbilityRuleData(AbilityRuleType.SingleTargetBuff, 0f, false, "지휘의 목소리") },
            { "keepfighting", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "쳐부숴라") },
            { "airofauthority", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "권위의 기운") },

            // === Fighter/Warrior 스킬 ===
            { "tauntingscream", new AbilityRuleData(AbilityRuleType.Normal, 0f, false, "조롱하는 외침 - AoE 도발") },
            { "fighterendure", new AbilityRuleData(AbilityRuleType.StackingBuff, 0f, false, "인내 - 방어 버프") },
            { "recklessstrike", new AbilityRuleData(AbilityRuleType.PreAttackBuff, 0f, false, "무모한 일격") },
            { "defensivestance", new AbilityRuleData(AbilityRuleType.StackingBuff, 0f, false, "방어 태세") },

            // === Psyker 위험한 광역 스킬 (아군 히트 시 절대 금지) ===
            // 눈꺼풀 없는 응시 (Lidless Stare) - 부채꼴 광역 공격
            // 아군이 하나라도 범위에 있으면 절대 사용 금지
            { "lidlessstare", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "눈꺼풀 없는 응시 - 아군 히트 금지") },
            { "lidless", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "눈꺼풀 없는 응시 - 아군 히트 금지") },
            { "stare", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "광역 시선 공격 - 아군 히트 금지") },

            // 기타 위험한 Psyker 광역 스킬들
            { "smite", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "스마이트 - 아군 히트 금지") },
            { "firestorm", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "화염폭풍 - 아군 히트 금지") },
            { "warpfire", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "워프 화염 - 아군 히트 금지") },

            // === 추가 위험한 광역 스킬들 (DangerousAoE) ===
            // Pyromancy 라인/광역 공격
            { "moltenbeam", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "용암 광선 - 라인 공격, 아군 히트 금지") },
            { "immolate", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "소각 - 라인 공격, 아군 히트 금지") },
            { "immolatesoul", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "영혼 소각 - 라인 공격, 아군 히트 금지") },

            // Psyker 콘/광역 공격
            { "psychicassault", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "사이킥 폭격 - 콘 공격, 아군 히트 금지") },
            { "scourgeoftheredtide", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "붉은 파도 - 구역 피해, 아군 히트 금지") },
            { "zoneoffear", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "공포의 구역 - 적 밀어내기, 아군 영향 가능") },
            { "spotofapathy", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "무관심의 지점 - 기절 구역, 아군 영향 가능") },

            // v2.0.7: 위키 검증 후 추가된 위험한 광역 스킬
            { "wildfire", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "들불 - 화염 확산 광역, 아군 히트 금지") },
            { "dangerousneighbourhood", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "위험한 이웃 - 광역 공격, 아군 히트 금지") },
            { "visionsofhell", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "지옥의 환영 - 광역 정신 공격, 아군 영향 가능") },
            { "warpcurseunleashed", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "워프 저주 해방 - 광역 저주, 아군 영향 가능") },

            // v2.0.10: Adept 궁극기 (실체 공격) - 광역 사이킥이지만 IsAoE=False로 잘못 감지됨
            { "adeptultimate", new AbilityRuleData(AbilityRuleType.DangerousAoE, 0f, false, "실체 공격 - 광역 사이킥 공격, 아군 히트 금지") },

            // === 무작위 근접 공격 스킬 (RandomMeleeAoE) ===
            // 근처 1칸 내 무작위 대상을 공격하며 진영(faction) 체크가 없음!
            // 아군이 근접해 있으면 아군도 맞을 수 있으므로 사용 금지

            // Blade Dance (칼날 춤) - ReaperBladeDanceAbility
            // 자신 위치에서 1칸 내 무작위 대상에게 4회 공격 (게임 코드: AbilityCustomBladeDance)
            { "bladedance", new AbilityRuleData(AbilityRuleType.RandomMeleeAoE, 0f, false, "칼날 춤 - 근처 무작위 대상 공격, 아군 피격 가능") },
            { "reaperbladedance", new AbilityRuleData(AbilityRuleType.RandomMeleeAoE, 0f, false, "칼날 춤 - 근처 무작위 대상 공격, 아군 피격 가능") },

            // Death Waltz (죽음의 왈츠) - Death From Above 반복 사용
            // 여러 적에게 도약 공격, 무작위 선택
            { "deathwaltz", new AbilityRuleData(AbilityRuleType.RandomMeleeAoE, 0f, false, "죽음의 왈츠 - 무작위 적에게 도약 공격") },

            // Orchestrated Firestorm (지휘된 화염폭풍) - 아군들이 무작위 적에게 공격
            { "orchestratedfirestorm", new AbilityRuleData(AbilityRuleType.Normal, 0f, false, "지휘된 화염폭풍 - 아군 조율 공격") },

            // Wild Hunt (야생 사냥) - 무작위 적에게 다중 크리티컬
            { "wildhunt", new AbilityRuleData(AbilityRuleType.Normal, 0f, false, "야생 사냥 - 무작위 적에게 크리티컬") },

            // === 추가 자해 스킬 (SelfDamage) ===
            // 전투 중 HP 관리 필요

            // Exsanguination (피 흘리기) - 아군에게 출혈을 주고 보너스 획득
            { "exsanguination", new AbilityRuleData(AbilityRuleType.SelfDamage, 50f, false, "피 흘리기 - 아군에게 출혈, HP 주의") },

            // At All Costs (어떤 대가를 치르더라도) - 아군을 쏴서 효과 부여
            { "atallcosts", new AbilityRuleData(AbilityRuleType.SelfDamage, 60f, true, "어떤 대가를 치르더라도 - 아군 사격") },

            // Carnival of Misery (고통의 축제) - DoT 피해 배가
            { "carnivalofmisery", new AbilityRuleData(AbilityRuleType.SelfDamage, 40f, false, "고통의 축제 - DoT 강화, 필사") },

            // Heroic Sacrifice (영웅적 희생) - 출혈 페널티
            { "heroicsacrifice", new AbilityRuleData(AbilityRuleType.SelfDamage, 50f, false, "영웅적 희생 - 출혈 페널티") },

            // === 필사 스킬 추가 (Desperate) ===
            // HP가 낮을 때만 사용

            // Dispatch (처형) - Desperate 버전
            { "dispatchdesperate", new AbilityRuleData(AbilityRuleType.Desperate, 30f, false, "필사 처형 - HP 낮을 때만") },

            // Firearm Mastery (화기 숙달) - Desperate 버전
            { "firearmsmastery_desperate", new AbilityRuleData(AbilityRuleType.Desperate, 30f, false, "필사 화기 숙달") },

            // === 마무리 스킬 추가 (Finisher) ===
            // 적 HP가 낮을 때 보너스

            // Killing Edge (살인의 칼날)
            { "killingedge", new AbilityRuleData(AbilityRuleType.Finisher, 0f, false, "살인의 칼날 - 적 HP 낮을 때 보너스") },

            // Finish the Job (마무리)
            { "finishthejob", new AbilityRuleData(AbilityRuleType.Finisher, 0f, false, "마무리 - 약한 적 우선") }
        };

        /// <summary>
        /// 스킬 규칙 데이터
        /// </summary>
        class AbilityRuleData
        {
            public AbilityRuleType Type;
            public float MinHPPercent;      // 이 HP% 이상일 때만 사용
            public bool SingleUsePerCombat; // 전투당 한 번만 사용
            public string Description;

            public AbilityRuleData(AbilityRuleType type, float minHP, bool singleUse, string desc)
            {
                Type = type;
                MinHPPercent = minHP;
                SingleUsePerCombat = singleUse;
                Description = desc;
            }
        }

        // 전투 중 사용된 스킬 추적 (유닛ID -> 사용한 스킬 블루프린트 이름 Set)
        private static Dictionary<string, HashSet<string>> _usedAbilitiesThisCombat = new();
        private static int _lastCombatRound = -1;

        // DangerousAoE 차단 추적 - 한 번 차단되면 해당 턴 동안 계속 차단
        // Key: "유닛ID_능력이름", Value: 차단된 라운드
        private static Dictionary<string, int> _blockedDangerousAoEThisTurn = new();
        private static string _lastTurnUnitId = "";

        /// <summary>
        /// 전투 시작/라운드 체크하여 추적 초기화
        /// </summary>
        static void CheckCombatReset()
        {
            try
            {
                var turnController = Game.Instance?.TurnController;
                if (turnController == null) return;

                int currentRound = turnController.CombatRound;

                // 새 전투 시작 (라운드 1) 또는 라운드가 리셋됨
                if (currentRound < _lastCombatRound || currentRound == 1 && _lastCombatRound != 1)
                {
                    _usedAbilitiesThisCombat.Clear();
                    Main.LogDebug("[CustomAI] Combat reset - clearing used abilities tracker");
                }
                _lastCombatRound = currentRound;
            }
            catch { /* 무시 */ }
        }

        /// <summary>
        /// 스킬 사용 기록
        /// </summary>
        static void RecordAbilityUsed(string unitId, string abilityBlueprintName)
        {
            if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(abilityBlueprintName)) return;

            if (!_usedAbilitiesThisCombat.ContainsKey(unitId))
                _usedAbilitiesThisCombat[unitId] = new HashSet<string>();

            // 블루프린트 이름 정규화 (소문자 + 언더스코어 제거)
            string normalizedName = NormalizeBlueprintName(abilityBlueprintName);
            _usedAbilitiesThisCombat[unitId].Add(normalizedName);

            // 현재 추적 중인 모든 스킬 표시
            string allUsed = string.Join(", ", _usedAbilitiesThisCombat[unitId]);
            Main.Log($"[CustomAI] RECORDED: {normalizedName} (original: {abilityBlueprintName})");
            Main.Log($"[CustomAI] Unit {unitId.Substring(0, Math.Min(8, unitId.Length))} used abilities: [{allUsed}]");
        }

        /// <summary>
        /// 스킬이 이미 사용되었는지 확인
        /// </summary>
        static bool WasAbilityUsedThisCombat(string unitId, string abilityBlueprintName)
        {
            if (string.IsNullOrEmpty(unitId) || string.IsNullOrEmpty(abilityBlueprintName)) return false;

            if (_usedAbilitiesThisCombat.TryGetValue(unitId, out var usedSet))
            {
                string normalizedName = NormalizeBlueprintName(abilityBlueprintName);
                bool wasUsed = usedSet.Contains(normalizedName);

                // 디버그: 현재 저장된 목록과 비교
                if (!wasUsed && usedSet.Count > 0)
                {
                    string allUsed = string.Join(", ", usedSet);
                    Main.LogDebug($"[CustomAI] Checking {normalizedName} against used: [{allUsed}]");
                }

                return wasUsed;
            }

            return false;
        }

        /// <summary>
        /// 블루프린트 이름 정규화 (일관된 비교를 위해)
        /// </summary>
        static string NormalizeBlueprintName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.ToLower().Replace("_", "").Replace("-", "").Replace(" ", "");
        }

        /// <summary>
        /// 블루프린트 이름으로 스킬 규칙 찾기
        /// </summary>
        static AbilityRuleData GetAbilityRule(string blueprintName)
        {
            if (string.IsNullOrEmpty(blueprintName)) return null;

            // 정규화 (소문자 + 특수문자 제거)
            string normalizedName = NormalizeBlueprintName(blueprintName);

            foreach (var kvp in AbilityRules)
            {
                // 규칙 키도 정규화해서 비교
                string normalizedKey = NormalizeBlueprintName(kvp.Key);
                if (normalizedName.Contains(normalizedKey))
                {
                    Main.Log($"[CustomAI] RULE MATCHED: {blueprintName} -> {kvp.Key} (Type: {kvp.Value.Type}, SingleUse: {kvp.Value.SingleUsePerCombat})");
                    return kvp.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// 위험한 광역 스킬인지 확인 (아군 히트 시 절대 금지)
        /// DangerousAoE: 일반적인 광역 스킬 (아군이 범위에 있으면 금지)
        /// RandomMeleeAoE: 무작위 근접 공격 (아군이 1칸 내에 있으면 금지)
        /// </summary>
        static bool IsDangerousAoE(AbilityData ability)
        {
            if (ability == null) return false;

            string abilityName = ability.Blueprint?.name ?? ability.Name ?? "";
            var rule = GetAbilityRule(abilityName);

            // 1. AbilityRules 딕셔너리에 등록된 DangerousAoE/RandomMeleeAoE 확인
            if (rule?.Type == AbilityRuleType.DangerousAoE ||
                rule?.Type == AbilityRuleType.RandomMeleeAoE)
            {
                return true;
            }

            // 2. v2.0.10: 컴포넌트 기반 자동 감지
            // 딕셔너리에 없어도 위험한 컴포넌트를 가진 능력은 위험으로 판단
            try
            {
                var components = ability.Blueprint?.ComponentsArray;
                if (components != null)
                {
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        string typeName = comp.GetType().Name;

                        // 진영 체크 없이 다중 타겟을 공격하는 위험한 컴포넌트들
                        if (typeName.Contains("Scatter") ||
                            typeName.Contains("BladeDance") ||
                            typeName.Contains("CustomRam") ||
                            typeName.Contains("StepThrough"))
                        {
                            Main.LogDebug($"[CustomAI] Auto-detected dangerous component: {typeName} in {abilityName}");
                            return true;
                        }
                    }
                }

                // 3. v2.0.10: 블루프린트 이름 기반 위험 감지
                // Ultimate 능력들은 대부분 광역 공격이며 아군 히트 가능
                string lowerName = abilityName.ToLower();
                if (lowerName.Contains("ultimate") ||
                    lowerName.Contains("궁극") ||
                    lowerName.Contains("실체"))
                {
                    Main.LogDebug($"[CustomAI] Auto-detected dangerous ability by name: {abilityName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] IsDangerousAoE component check error: {ex.Message}");
            }

            return false;
        }

        #endregion

        #region Main Patch

        [HarmonyPatch(typeof(TaskNodeSelectAbilityTarget))]
        public static class SelectAbilityTargetOverride
        {
            [HarmonyPatch("TickInternal")]
            [HarmonyPrefix]
            static bool Prefix(TaskNodeSelectAbilityTarget __instance, Blackboard blackboard, ref Status __result)
            {
                try
                {
                    var context = blackboard?.DecisionContext;
                    if (context == null) return true;

                    var unit = context.Unit;
                    if (unit == null || !unit.IsDirectlyControllable) return true;

                    var settings = Main.Settings.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
                    if (!settings.EnableCustomAI) return true;

                    __result = EnhancedSelectAbilityAndTarget(context, unit, settings);
                    return false;
                }
                catch (Exception ex)
                {
                    Main.LogError($"[CustomAI] SelectAbilityTarget error: {ex}");
                    return true;
                }
            }

            /// <summary>
            /// 고도화된 어빌리티/타겟 선택 로직
            /// </summary>
            static Status EnhancedSelectAbilityAndTarget(DecisionContext context, BaseUnitEntity unit, Settings.CharacterSettings settings)
            {
                context.AbilityTarget = null;
                context.Ability = null;

                // 0. 전투 리셋 체크 (새 전투 시작 시 스킬 사용 추적 초기화)
                CheckCombatReset();

                // 1. 전투 상황 분석
                var analysis = AnalyzeCombatSituation(context, unit);

                // 2. 설정에서 유닛 역할 가져오기 (자동 감지 대신 수동 설정 사용)
                var role = GetRoleFromSettings(settings);
                analysis.UnitRole = role;

                // 3. 사용 가능한 어빌리티 가져오기 및 분류
                var abilities = GetAndCategorizeAbilities(context, unit, settings, analysis);
                if (abilities.Count == 0)
                {
                    Main.LogDebug($"[CustomAI] {unit.CharacterName}: No usable abilities");
                    return Status.Failure;
                }

                string archetypeInfo = analysis.Archetype != GameArchetype.Unknown ? $", Archetype: {analysis.Archetype}" : "";
                Main.LogDebug($"[CustomAI] {unit.CharacterName} (Role: {role}{archetypeInfo}, Phase: {analysis.Phase}): {abilities.Count} abilities");

                // 4. 역할 및 상황 기반 최적 선택
                var (bestAbility, bestTarget) = SelectBestAction(context, unit, settings, abilities, analysis);

                if (bestAbility != null && bestTarget != null)
                {
                    context.Ability = bestAbility;
                    context.AbilityTarget = bestTarget;

                    // 5. 스킬 사용 기록 (단일 사용 스킬 추적)
                    string bpName = bestAbility.Blueprint?.name;
                    if (!string.IsNullOrEmpty(bpName))
                    {
                        var rule = GetAbilityRule(bpName);
                        if (rule != null && rule.SingleUsePerCombat)
                        {
                            Main.Log($"[CustomAI] >>> RECORDING SINGLE-USE: {bpName} for unit {unit.UniqueId}");
                            RecordAbilityUsed(unit.UniqueId, bpName);
                        }
                    }

                    Main.Log($"[CustomAI] FINAL DECISION: {unit.CharacterName} (ID: {unit.UniqueId?.Substring(0, Math.Min(12, unit.UniqueId?.Length ?? 0))}): {bestAbility.Name} -> {GetTargetName(bestTarget)}");
                    return Status.Success;
                }

                Main.LogDebug($"[CustomAI] {unit.CharacterName}: No valid action found");
                return Status.Failure;
            }
        }

        #endregion

        #region Combat Analysis

        /// <summary>
        /// 전투 상황 종합 분석
        /// </summary>
        static CombatAnalysis AnalyzeCombatSituation(DecisionContext context, BaseUnitEntity unit)
        {
            var analysis = new CombatAnalysis();

            try
            {
                // 적/아군 수집
                CollectUnits(context, analysis);

                // 자신의 상태
                analysis.UnitHPPercent = GetHPPercent(unit);
                analysis.IsEngaged = unit.CombatState?.IsEngaged ?? false;

                // 아키타입 감지 (어빌리티 기반)
                analysis.Archetype = DetectArchetype(unit);

                // 가장 가까운/약한 적 찾기
                FindKeyEnemies(unit, analysis);

                // 아군 상태 분석
                AnalyzeAllyStatus(unit, analysis);

                // 전투 단계 판단
                analysis.Phase = DetermineCombatPhase(analysis);

                // 팀 집중 타겟 (가장 많이 공격받고 있는 적)
                analysis.FocusTarget = DetermineFocusTarget(analysis);
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] AnalyzeCombatSituation error: {ex}");
            }

            return analysis;
        }

        static void CollectUnits(DecisionContext context, CombatAnalysis analysis)
        {
            var unit = context.Unit;
            bool unitIsPlayer = unit.IsPlayerFaction;

            // context.Enemies/Allies는 신뢰할 수 없음 (게임 AI가 플레이어 유닛을 적으로 취급)
            // 직접 팩션으로 판별해야 함

            // 모든 전투 유닛을 순회하며 적/아군 분류
            try
            {
                var allUnits = Game.Instance?.State?.AllBaseAwakeUnits;
                if (allUnits != null)
                {
                    foreach (var other in allUnits)
                    {
                        if (other == null || other == unit) continue;
                        if (other.LifeState.IsDead) continue;
                        if (!other.IsInCombat) continue;

                        bool otherIsPlayer = other.IsPlayerFaction;

                        // 같은 팩션이면 아군, 다르면 적
                        if (unitIsPlayer == otherIsPlayer)
                        {
                            analysis.Allies.Add(other);
                        }
                        else
                        {
                            analysis.Enemies.Add(other);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] CollectUnits error: {ex}");

                // 폴백: context 사용 (문제 있을 수 있음)
                if (context.Enemies != null)
                {
                    foreach (var targetInfo in context.Enemies)
                    {
                        var enemy = targetInfo.Entity as BaseUnitEntity;
                        if (enemy != null && !enemy.LifeState.IsDead)
                        {
                            // 플레이어 팩션이면 적으로 추가하지 않음!
                            if (!enemy.IsPlayerFaction || !unitIsPlayer)
                                analysis.Enemies.Add(enemy);
                        }
                    }
                }

                if (context.Allies != null)
                {
                    foreach (var targetInfo in context.Allies)
                    {
                        var ally = targetInfo.Entity as BaseUnitEntity;
                        if (ally != null && !ally.LifeState.IsDead)
                            analysis.Allies.Add(ally);
                    }
                }
            }

            Main.LogDebug($"[CustomAI] CollectUnits: {analysis.Enemies.Count} enemies, {analysis.Allies.Count} allies (unit is player: {unitIsPlayer})");
        }

        static void FindKeyEnemies(BaseUnitEntity unit, CombatAnalysis analysis)
        {
            float minDist = float.MaxValue;
            float minHP = float.MaxValue;
            int nearbyCount = 0;
            const float NEARBY_RANGE = 10f; // 도발/근접 범위
            const float MELEE_THREAT_RANGE = 5f; // 근접 위협 범위

            float maxThreat = 0f;
            BaseUnitEntity highestThreatEnemy = null;
            bool needsKiting = false;
            BaseUnitEntity kitingThreat = null;

            // 게임의 수집된 데이터 접근 시도
            UnitDataStorage aiDataStorage = null;
            try
            {
                aiDataStorage = Game.Instance?.Player?.AiCollectedDataStorage;
            }
            catch { }

            foreach (var enemy in analysis.Enemies)
            {
                float dist = Vector3.Distance(unit.Position, enemy.Position);
                float hp = GetHPPercent(enemy);

                if (dist < minDist)
                {
                    minDist = dist;
                    analysis.NearestEnemy = enemy;
                    analysis.NearestEnemyDistance = dist;
                }

                if (hp < minHP)
                {
                    minHP = hp;
                    analysis.WeakestEnemy = enemy;
                    analysis.WeakestEnemyHP = hp;
                }

                // 근처 적 카운트
                if (dist <= NEARBY_RANGE)
                {
                    nearbyCount++;
                }

                // 위협도 계산
                float threatScore = CalculateEnemyThreat(enemy, unit, dist, aiDataStorage);
                analysis.EnemyThreatScores[enemy] = threatScore;

                if (threatScore > maxThreat)
                {
                    maxThreat = threatScore;
                    highestThreatEnemy = enemy;
                }

                // 카이팅 필요 여부 체크 (원거리 캐릭터가 근접 위협에 노출)
                if (analysis.UnitRole == UnitRole.Sniper || analysis.UnitRole == UnitRole.Support)
                {
                    if (dist <= MELEE_THREAT_RANGE && IsMeleeUnit(enemy))
                    {
                        needsKiting = true;
                        if (kitingThreat == null || dist < Vector3.Distance(unit.Position, kitingThreat.Position))
                        {
                            kitingThreat = enemy;
                        }
                    }
                }
            }

            analysis.NearbyEnemies = nearbyCount;
            analysis.HighestThreatEnemy = highestThreatEnemy;
            analysis.HighestThreatScore = maxThreat;
            analysis.NeedsKiting = needsKiting;
            analysis.KitingThreat = kitingThreat;

            if (highestThreatEnemy != null)
            {
                Main.LogDebug($"[ThreatAssess] {unit.CharacterName}: Highest threat = {highestThreatEnemy.CharacterName} (score: {maxThreat:F1})");
            }
            if (needsKiting)
            {
                Main.LogDebug($"[Kiting] {unit.CharacterName}: Needs to kite away from {kitingThreat?.CharacterName}");
            }
        }

        /// <summary>
        /// 적의 위협도 계산
        /// </summary>
        static float CalculateEnemyThreat(BaseUnitEntity enemy, BaseUnitEntity unit, float distance, UnitDataStorage aiDataStorage)
        {
            float threat = 0f;

            try
            {
                // 1. 게임의 수집된 공격 데이터 사용 (가장 신뢰성 높음)
                if (aiDataStorage != null)
                {
                    try
                    {
                        var collectedData = aiDataStorage[enemy];
                        if (collectedData != null)
                        {
                            int threatRange = collectedData.AttackDataCollection.GetThreatRange();

                            // 위협 범위 내에 있으면 높은 위협
                            if (distance <= threatRange)
                            {
                                threat += 30f;
                            }
                            else if (distance <= threatRange + enemy.CombatState.ActionPointsBlue)
                            {
                                threat += 15f; // 다음 턴에 공격 가능
                            }
                        }
                    }
                    catch { }
                }

                // 2. 적의 무기 기반 위협도
                try
                {
                    var weapon = enemy.Body?.PrimaryHand?.MaybeWeapon;
                    if (weapon != null)
                    {
                        // 무기 데미지 기반 위협도 평가
                        int weaponDamage = weapon.Blueprint?.WarhammerDamage ?? 0;
                        threat += weaponDamage * 0.5f;

                        // 원거리 무기면 거리와 무관하게 위협
                        int weaponRange = weapon.Blueprint?.AttackRange ?? 1;
                        if (weaponRange > 5)
                        {
                            threat += 10f;
                        }
                    }
                }
                catch { }

                // 3. 거리 기반 (가까울수록 위협)
                threat += Mathf.Max(0, 20f - distance * 2f);

                // 4. HP 기반 (풀피 적이 더 위협적)
                float enemyHP = GetHPPercent(enemy);
                threat += enemyHP * 0.1f;

                // 5. 교전 상태 (우리를 타겟팅 중이면 더 위협)
                if (enemy.CombatState?.ManualTarget == unit)
                {
                    threat += 20f;
                }

                // 6. 근접 유닛이 가까이 있으면 더 위협적 (카이팅 필요)
                if (IsMeleeUnit(enemy) && distance <= 5f)
                {
                    threat += 15f;
                }
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[ThreatAssess] Error calculating threat for {enemy.CharacterName}: {ex.Message}");
            }

            return threat;
        }

        /// <summary>
        /// 근접 유닛인지 판별
        /// </summary>
        static bool IsMeleeUnit(BaseUnitEntity unit)
        {
            try
            {
                // 어빌리티에서 판별
                foreach (var ability in unit.Abilities)
                {
                    if (ability?.Data?.Blueprint == null) continue;
                    var range = ability.Data.Blueprint.Range;

                    // 무기 어빌리티이고 사거리가 짧으면 근접
                    if (ability.Data.Blueprint.name.Contains("Attack") || ability.Data.Blueprint.name.Contains("Strike"))
                    {
                        if (ability.Data.RangeCells <= 2)
                        {
                            return true;
                        }
                    }
                }

                // 무기로 판별
                var weapon = unit.Body?.PrimaryHand?.MaybeWeapon;
                if (weapon != null)
                {
                    return weapon.Blueprint?.AttackRange <= 2;
                }
            }
            catch { }

            return false;
        }

        static void AnalyzeAllyStatus(BaseUnitEntity unit, CombatAnalysis analysis)
        {
            float minAllyHP = 100f;
            BaseUnitEntity weakestAlly = null;
            int criticalAllies = 0;
            int woundedAllies = 0;

            // 자신 포함
            var allAllies = new List<BaseUnitEntity>(analysis.Allies) { unit };

            foreach (var ally in allAllies)
            {
                float hp = GetHPPercent(ally);

                if (hp < minAllyHP)
                {
                    minAllyHP = hp;
                    weakestAlly = ally;
                }

                if (hp < 30f) criticalAllies++;
                else if (hp < 70f) woundedAllies++;
            }

            analysis.WeakestAlly = weakestAlly;
            analysis.WeakestAllyHP = minAllyHP;
            analysis.CriticalAlliesCount = criticalAllies;
            analysis.WoundedAlliesCount = woundedAllies;
            analysis.TeamNeedsHealing = criticalAllies > 0 || woundedAllies >= 2;
        }

        static CombatPhase DetermineCombatPhase(CombatAnalysis analysis)
        {
            // 적 대부분이 약함 = 마무리 단계
            int weakEnemies = analysis.Enemies.Count(e => GetHPPercent(e) < 40f);
            if (weakEnemies >= analysis.Enemies.Count * 0.6f)
                return CombatPhase.Cleanup;

            // 적 대부분이 건강함 = 시작 단계
            int healthyEnemies = analysis.Enemies.Count(e => GetHPPercent(e) > 80f);
            if (healthyEnemies >= analysis.Enemies.Count * 0.7f)
                return CombatPhase.Opening;

            return CombatPhase.Mid;
        }

        static BaseUnitEntity DetermineFocusTarget(CombatAnalysis analysis)
        {
            // 가장 약한 적을 집중 타겟으로 (마무리 우선)
            if (analysis.WeakestEnemy != null && analysis.WeakestEnemyHP < 50f)
                return analysis.WeakestEnemy;

            // 그 외에는 가장 가까운 적
            return analysis.NearestEnemy;
        }

        #endregion

        #region Role Detection

        /// <summary>
        /// 설정에서 역할 가져오기 (UI에서 수동 설정)
        /// </summary>
        static UnitRole GetRoleFromSettings(Settings.CharacterSettings settings)
        {
            // Settings.AIRole -> UnitRole 매핑
            return settings.Role switch
            {
                Settings.AIRole.Balanced => UnitRole.Balanced,
                Settings.AIRole.Tank => UnitRole.Tank,
                Settings.AIRole.DPS => UnitRole.DPS,
                Settings.AIRole.Support => UnitRole.Support,
                Settings.AIRole.Hybrid => UnitRole.Hybrid,
                Settings.AIRole.Sniper => UnitRole.Sniper,
                _ => UnitRole.Balanced
            };
        }

        /// <summary>
        /// 유닛의 어빌리티로 아키타입 감지
        /// </summary>
        static GameArchetype DetectArchetype(BaseUnitEntity unit)
        {
            if (unit?.Abilities == null) return GameArchetype.Unknown;

            var abilityNames = new List<string>();
            foreach (var ability in unit.Abilities.Enumerable)
            {
                // 블루프린트 이름 사용 (영문)
                if (ability?.Data?.Blueprint?.name != null)
                    abilityNames.Add(ability.Data.Blueprint.name.ToLower());
                // 블루프린트 AssetGuid도 확인
                if (ability?.Data?.Blueprint?.AssetGuid != null)
                    abilityNames.Add(ability.Data.Blueprint.AssetGuid.ToLower());
            }

            // 각 아키타입별 매칭 점수 계산
            var scores = new Dictionary<GameArchetype, int>();
            foreach (var kvp in ArchetypeAbilityKeywords)
            {
                int score = 0;
                foreach (var keyword in kvp.Value)
                {
                    foreach (var abilityName in abilityNames)
                    {
                        if (abilityName.Contains(keyword))
                            score++;
                    }
                }
                if (score > 0)
                    scores[kvp.Key] = score;
            }

            // 가장 높은 점수의 아키타입 반환
            if (scores.Count > 0)
            {
                var best = scores.OrderByDescending(x => x.Value).First();
                if (best.Value >= 2) // 최소 2개 이상 매칭되어야 확정
                {
                    Main.LogDebug($"[CustomAI] Archetype detected: {best.Key} (score: {best.Value})");
                    return best.Key;
                }
            }

            return GameArchetype.Unknown;
        }

        /// <summary>
        /// 아키타입 기반 추천 역할
        /// </summary>
        static UnitRole GetRecommendedRoleFromArchetype(GameArchetype archetype)
        {
            return archetype switch
            {
                GameArchetype.Vanguard => UnitRole.Tank,
                GameArchetype.Warrior => UnitRole.Tank,
                GameArchetype.Officer => UnitRole.Support,
                GameArchetype.Operative => UnitRole.DPS,
                GameArchetype.Soldier => UnitRole.Sniper,
                GameArchetype.Bladedancer => UnitRole.DPS,
                GameArchetype.Assassin => UnitRole.DPS,
                GameArchetype.BountyHunter => UnitRole.DPS,
                GameArchetype.MasterTactician => UnitRole.DPS,
                GameArchetype.GrandStrategist => UnitRole.Support,
                GameArchetype.ArchMilitant => UnitRole.Hybrid,
                GameArchetype.Executioner => UnitRole.DPS,
                GameArchetype.Overseer => UnitRole.Support,
                GameArchetype.Psyker => UnitRole.Support,
                GameArchetype.TechPriest => UnitRole.Support,
                _ => UnitRole.Balanced
            };
        }

        /// <summary>
        /// 어빌리티 카테고리 분류 (게임 특화)
        /// </summary>
        static AbilityCategory CategorizeAbility(AbilityData ability)
        {
            if (ability?.Blueprint == null) return AbilityCategory.Unknown;

            var blueprint = ability.Blueprint;
            // 한글 이름 + 블루프린트 이름(영문) 모두 검사
            string localizedName = ability.Name?.ToLower() ?? "";
            string blueprintName = blueprint.name?.ToLower() ?? "";
            string combinedName = localizedName + " " + blueprintName;

            // === 게임 특화 키워드 우선 체크 ===
            // 주의: 키워드 순서가 중요! 더 구체적인 것을 먼저 체크

            // =====================================================
            // === 1. 명시적 공격 스킬 (최우선 - 다른 키워드와 혼동 방지) ===
            // =====================================================

            // --- Bladedancer/Reaper 공격 스킬 ---
            if (ContainsAny(combinedName, "bladedance", "blade dance", "칼날 춤", "reaperbladedance"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "deathwaltz", "death waltz", "죽음의 왈츠"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "dansemacabre", "danse macabre", "죽음의 춤"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "deathfromabove", "death from above", "상공에서의 죽음"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "deathwhisper", "death whisper", "죽음의 속삭임"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "killingedge", "killing edge"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "captiveaudience", "captive audience"))
                return AbilityCategory.Attack;

            // --- Assassin/Executioner 공격 스킬 ---
            if (ContainsAny(combinedName, "dispatch", "처형"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "deathblow", "death blow", "죽음의 일격"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "terrifyingstrike", "terrifying strike"))
                return AbilityCategory.Attack;

            // --- Soldier/Warrior 공격 스킬 (Movement와 혼동 방지) ---
            // Charge는 이동+공격이지만 공격으로 분류 (적을 타겟으로 함)
            if (ContainsAny(combinedName, "fightercharge", "fighter_charge", "warrior_charge"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "breakthrough", "break through", "돌파"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "assaultonslaught", "assault onslaught"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "forcefulstrike", "forceful strike", "강력한 일격"))
                return AbilityCategory.Attack;
            // Reckless Strike - "reckless"가 Buff에 있으므로 먼저 체크
            if (ContainsAny(combinedName, "recklessstrike", "reckless strike", "reckless_strike", "무모한 일격"))
                return AbilityCategory.Attack;

            // --- Psyker 공격 스킬 ---
            if (ContainsAny(combinedName, "psychicscream", "psychic scream", "사이킥 비명", "psychicshriek", "psychic shriek"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "psychicassault", "psychic assault", "사이킥 폭격"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "purgesoul", "purge soul", "영혼 정화"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "evileye", "evil eye", "사악한 눈"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "dominate", "지배"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "mindrupture", "mind rupture", "정신 파열"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "sensorydeprivation", "sensory deprivation"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "wakingnightmare", "waking nightmare"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "heldingaze", "held in my gaze", "heldinmygaze"))
                return AbilityCategory.Attack;

            // --- Pyromancy 공격 스킬 ---
            if (ContainsAny(combinedName, "ignite", "점화"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "incinerate", "소각"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "inflame", "불태우기"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "moltenbeam", "molten beam", "용암 광선"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "immolate", "immolatesoul", "immolate the soul"))
                return AbilityCategory.Attack;

            // --- Soldier 공격 스킬 ---
            if (ContainsAny(combinedName, "doubleslug", "double slug", "더블 슬러그"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "secondshot", "second shot", "두번째 사격"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "rapidfire", "rapid fire", "속사"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "piercingshot", "piercing shot", "관통 사격"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "finishthejob", "finish the job"))
                return AbilityCategory.Attack;

            // --- Bounty Hunter 공격 스킬 ---
            if (ContainsAny(combinedName, "claimthebounty", "claim the bounty", "현상금 수금"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "wildhunt", "wild hunt", "야생 사냥"))
                return AbilityCategory.Attack;

            // --- Officer 공격 명령 스킬 ---
            if (ContainsAny(combinedName, "orchestratedfirestorm", "orchestrated firestorm"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "lastvolley", "last volley", "마지막 일제사격"))
                return AbilityCategory.Attack;

            // --- Familiar 공격 스킬 ---
            if (ContainsAny(combinedName, "apprehend", "체포"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "bite!", "bite"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "blindingstrike", "blinding strike"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "strafe", "기총소사"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "obstructvision", "obstruct vision"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "purificationdischarge", "purification discharge"))
                return AbilityCategory.Attack;

            // --- Techmarine 공격 스킬 ---
            if (ContainsAny(combinedName, "manipulatorpush", "manipulator push"))
                return AbilityCategory.Attack;

            // --- Tormentor 공격 스킬 ---
            if (ContainsAny(combinedName, "painresonance", "pain resonance"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "whereithurts", "where it hurts"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "mercilessverdict", "merciless verdict"))
                return AbilityCategory.Attack;

            // --- 기타 공격 스킬 ---
            if (ContainsAny(combinedName, "kick", "발차기"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "feintingattack", "feinting attack"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "devastatingattack", "devastating attack"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "scourgeoftheredtide", "scourge of the red tide"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "zoneoffear", "zone of fear"))
                return AbilityCategory.Attack;

            // --- v2.0.7: 위키 검증 후 추가된 공격 스킬 ---
            // Soldier/Ranged 공격 스킬
            if (ContainsAny(combinedName, "controlledshot", "controlled shot", "조준 사격"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "perfectshot", "perfect shot", "완벽한 사격"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "preciseattack", "precise attack", "정밀 공격"))
                return AbilityCategory.Attack;

            // Bounty Hunter 공격 스킬
            if (ContainsAny(combinedName, "huntdowntheprey", "hunt down the prey", "사냥감 추적"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "ensnaretheprey", "ensnare the prey", "사냥감 포획"))
                return AbilityCategory.Attack;

            // Pyromancy 광역 공격
            if (ContainsAny(combinedName, "wildfire", "산불", "들불"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "orchestrateflames", "orchestrate flames", "화염 지휘"))
                return AbilityCategory.Attack;

            // Officer/Command 공격 스킬
            if (ContainsAny(combinedName, "raid", "급습"))
                return AbilityCategory.Attack;

            // Psyker/Tormentor 공격 스킬
            if (ContainsAny(combinedName, "visionsofhell", "visions of hell", "지옥의 환영"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "giftoftorment", "gift of torment", "고통의 선물"))
                return AbilityCategory.Attack;
            if (ContainsAny(combinedName, "warpcurseunleashed", "warp curse unleashed", "워프 저주 해방"))
                return AbilityCategory.Attack;

            // 위험한 광역 공격
            if (ContainsAny(combinedName, "dangerousneighbourhood", "dangerous neighbourhood", "위험한 이웃"))
                return AbilityCategory.Attack;

            // Ministorum Priest 공격 스킬
            if (ContainsAny(combinedName, "swordoffaith", "sword of faith", "신앙의 검"))
                return AbilityCategory.Attack;

            // =====================================================
            // === 2. 자해/특수 스킬 (공격이 아님, 별도 처리 필요) ===
            // =====================================================

            // At All Costs - 아군을 쏴서 효과를 주는 특수 스킬 (SelfDamage 카테고리로)
            // 이 스킬은 AbilityRules에서 별도 처리

            // =====================================================
            // === 3. 도발 스킬 ===
            // =====================================================

            if (ContainsAny(combinedName, "taunt", "provocation", "provoking", "도발"))
                return AbilityCategory.Taunt;
            if (combinedName.Contains("taunting") && combinedName.Contains("scream"))
                return AbilityCategory.Taunt;

            // =====================================================
            // === 4. 약점 공략 (Operative) ===
            // =====================================================

            if (ContainsAny(combinedName, "analyse", "analyze", "exploit", "dismantle", "weakness", "약점"))
                return AbilityCategory.Exploit;

            // =====================================================
            // === 5. 힐/재생 ===
            // =====================================================

            if (ContainsAny(combinedName, "heal", "restore", "mend", "cure", "regenerat", "medicae",
                "치유", "회복", "invigorate", "propheticintervention", "prophetic intervention", "lightoftheemperor"))
                return AbilityCategory.Heal;

            // =====================================================
            // === 6. 방어 스킬 ===
            // =====================================================

            if (ContainsAny(combinedName, "shield", "protect", "ward", "armor", "defence", "defense",
                "guard", "bulwark", "deflect", "defensive", "endure", "endurance", "stance",
                "braceforimpact", "brace for impact", "wallofrockcrete", "wall of rockcrete",
                "cautiousapproach", "cautious approach", "entrench", "veilofblades", "veil of blades"))
                return AbilityCategory.Defense;

            // =====================================================
            // === 7. 버프 스킬 (공격 키워드 제외) ===
            // =====================================================

            // "reckless"는 위에서 recklessstrike를 먼저 체크했으므로 여기서는 버프
            // "charge"는 위에서 fightercharge를 먼저 체크했으므로 안전
            if (ContainsAny(combinedName, "buff", "bless", "haste", "strength", "enhance", "empower",
                "inspire", "command", "order", "rally", "voice of", "unyielding", "beacon",
                "keystone", "frontline", "backline", "rear", "stratagem", "listen", "authority",
                "tacticaladvantage", "tactical advantage", "linchpin", "fervour", "fervour",
                "mindbond", "mind bond", "prescience", "foreboding", "precognition",
                "warpspeed", "warp speed", "followmylead", "follow my lead", "showthepath",
                "hammeroftheemperor", "hammer of the emperor", "shieldoftheemperor",
                "perfecttiming", "perfect timing", "regimental", "combatlocus", "blitzstratagem",
                "strongholdstratagem", "trenchlinestratagem", "overwhelmingstratagem", "killzonestratagem",
                "versatility", "elusiveshadow", "elusive shadow", "confidentapproach",
                "revealthelight", "reveal the light", "revelinslaughter", "revel in slaughter",
                "recklessrush", "reckless rush", "airofauthority", "air of authority"))
                return AbilityCategory.Buff;

            // =====================================================
            // === 8. 디버프 스킬 ===
            // =====================================================

            if (ContainsAny(combinedName, "curse", "weaken", "slow", "debuff", "reduce", "drain",
                "terrif", "fear", "suppress", "suppressing", "death sentence", "enfeeble",
                "warpcurse", "warp curse", "intimidation", "carnivalofmisery"))
                return AbilityCategory.Debuff;

            // =====================================================
            // === 9. 이동 스킬 (순수 이동만) ===
            // =====================================================

            // 주의: charge, assault, breakthrough 등은 위에서 공격으로 분류됨
            if (!combinedName.Contains("overcharge") &&
                ContainsAny(combinedName, "move", "teleport", "dash", "relocate", "consolidation",
                "acrobaticartistry", "acrobatic artistry", "soar"))
                return AbilityCategory.Movement;

            // 블루프린트 속성 기반 분류
            if (blueprint.CanTargetEnemies && !blueprint.CanTargetFriends)
                return AbilityCategory.Attack;

            if (blueprint.CanTargetFriends && !blueprint.CanTargetEnemies)
            {
                if (blueprint.CanTargetSelf && blueprint.Range == AbilityRange.Personal)
                    return AbilityCategory.Buff;
                return AbilityCategory.Heal; // 또는 버프
            }

            if (blueprint.CanTargetSelf && blueprint.Range == AbilityRange.Personal)
                return AbilityCategory.Buff;

            return AbilityCategory.Attack; // 기본값
        }

        static bool ContainsAny(string text, params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                if (text.Contains(keyword)) return true;
            }
            return false;
        }

        #endregion

        #region Ability Filtering & Scoring

        /// <summary>
        /// 사용 가능한 어빌리티 가져오기 및 분류
        /// </summary>
        static List<CategorizedAbility> GetAndCategorizeAbilities(
            DecisionContext context, BaseUnitEntity unit,
            Settings.CharacterSettings settings, CombatAnalysis analysis)
        {
            var result = new List<CategorizedAbility>();
            var profile = new AbilityProfile();

            try
            {
                var gameAbilities = context.GetSortedAbilityList(CastTimepointType.Any);
                if (gameAbilities == null) return result;

                // 첫 번째 라운드일 때만 전체 어빌리티 블루프린트 이름 출력
                if (Game.Instance?.TurnController?.CombatRound <= 1)
                {
                    Main.Log($"[CustomAI] === {unit.CharacterName} ABILITY LIST ===");
                    foreach (var ability in gameAbilities)
                    {
                        if (ability?.Blueprint == null) continue;
                        string bpName = ability.Blueprint.name ?? "NULL";
                        string localName = ability.Name ?? "NULL";
                        string guid = ability.Blueprint.AssetGuid?.ToString() ?? "NULL";
                        Main.Log($"[CustomAI] BP: {bpName} | Display: {localName} | GUID: {guid}");
                    }
                    Main.Log($"[CustomAI] === END ABILITY LIST ===");
                }

                foreach (var ability in gameAbilities)
                {
                    if (ability == null) continue;

                    // 스킵 전에 블루프린트 이름 로그
                    string blueprintName = ability.Blueprint?.name ?? "unknown";
                    var rule = GetAbilityRule(blueprintName);
                    if (rule != null)
                    {
                        Main.Log($"[CustomAI] Evaluating {ability.Name} (BP: {blueprintName}) - Rule: {rule.Type}, SingleUse: {rule.SingleUsePerCombat}");
                    }

                    if (ShouldSkipAbility(unit, ability, settings, analysis)) continue;

                    var category = CategorizeAbility(ability);
                    int apCost = ability.CalculateActionPointCost();
                    bool isMelee = IsMeleeAbility(ability);
                    bool isRanged = IsRangedAbility(ability);

                    // 프로필 업데이트
                    UpdateAbilityProfile(profile, category, isMelee, isRanged);

                    result.Add(new CategorizedAbility
                    {
                        Ability = ability,
                        Category = category,
                        APCost = apCost,
                        IsMelee = isMelee,
                        IsRanged = isRanged
                    });
                }

                // 프로필 저장
                analysis.Profile = profile;
                Main.LogDebug($"[CustomAI] {unit.CharacterName} Profile: {profile}");
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] GetAndCategorizeAbilities error: {ex}");
            }

            return result;
        }

        /// <summary>
        /// 어빌리티 프로필 업데이트
        /// </summary>
        static void UpdateAbilityProfile(AbilityProfile profile, AbilityCategory category, bool isMelee, bool isRanged)
        {
            switch (category)
            {
                case AbilityCategory.Attack:
                    if (isMelee) profile.MeleeAttacks++;
                    else if (isRanged) profile.RangedAttacks++;
                    else profile.RangedAttacks++; // 기본값은 원거리로
                    break;
                case AbilityCategory.Defense:
                    profile.DefenseSkills++;
                    break;
                case AbilityCategory.Taunt:
                    profile.TauntSkills++;
                    break;
                case AbilityCategory.Buff:
                    profile.BuffSkills++;
                    break;
                case AbilityCategory.Debuff:
                    profile.DebuffSkills++;
                    break;
                case AbilityCategory.Heal:
                    profile.HealSkills++;
                    break;
            }
        }

        /// <summary>
        /// 어빌리티 스킵 여부 (강화된 필터링)
        /// </summary>
        static bool ShouldSkipAbility(BaseUnitEntity unit, AbilityData ability,
            Settings.CharacterSettings settings, CombatAnalysis analysis)
        {
            string name = ability.Name?.ToLower() ?? "";
            string blueprintName = ability.Blueprint?.name ?? "";

            // === 스킬별 특수 규칙 체크 ===
            var rule = GetAbilityRule(blueprintName);
            if (rule != null)
            {
                // 1. 자해 스킬: HP 임계값 체크
                if (rule.Type == AbilityRuleType.SelfDamage)
                {
                    if (analysis.UnitHPPercent < rule.MinHPPercent)
                    {
                        Main.LogDebug($"[CustomAI] Skip {ability.Name}: HP {analysis.UnitHPPercent:F0}% < required {rule.MinHPPercent}%");
                        return true;
                    }
                }

                // 2. 단일 사용 스킬: 이미 사용했는지 체크
                if (rule.SingleUsePerCombat)
                {
                    string unitIdShort = unit.UniqueId?.Substring(0, Math.Min(12, unit.UniqueId?.Length ?? 0)) ?? "NULL";
                    bool wasUsed = WasAbilityUsedThisCombat(unit.UniqueId, blueprintName);
                    Main.Log($"[CustomAI] SINGLE-USE CHECK: {ability.Name} (BP: {blueprintName}) for unit {unitIdShort}, wasUsed={wasUsed}");
                    if (wasUsed)
                    {
                        Main.Log($"[CustomAI] >>> BLOCKED: {ability.Name} already used this combat by {unitIdShort}");
                        return true;
                    }
                    else
                    {
                        Main.Log($"[CustomAI] >>> ALLOWED: {ability.Name} not yet used by {unitIdShort}");
                    }
                }

                // 3. 필사 스킬: HP가 높으면 스킵 (HP 낮을 때만 사용)
                if (rule.Type == AbilityRuleType.Desperate)
                {
                    if (analysis.UnitHPPercent > rule.MinHPPercent)
                    {
                        Main.LogDebug($"[CustomAI] Skip {ability.Name}: HP too high for desperate ability");
                        return true;
                    }
                }

                // 4. 턴 종료 스킬: HP 임계값 체크 (방어용이므로)
                if (rule.Type == AbilityRuleType.TurnEnding)
                {
                    // HP가 충분하면 턴 종료 방어 스킬 불필요
                    if (analysis.UnitHPPercent > rule.MinHPPercent && !analysis.IsEngaged)
                    {
                        Main.LogDebug($"[CustomAI] Skip {ability.Name}: Turn-ending ability not needed (HP ok, not engaged)");
                        return true;
                    }
                }

                // 5. RandomMeleeAoE 스킬: 아군이 1칸 내에 있으면 절대 사용 금지!
                // Blade Dance 같은 스킬은 근처 무작위 대상을 공격하며 faction 체크가 없음
                if (rule.Type == AbilityRuleType.RandomMeleeAoE)
                {
                    bool hasAllyInMeleeRange = false;
                    foreach (var ally in analysis.Allies)
                    {
                        if (ally != null && ally != unit && !ally.LifeState.IsDead)
                        {
                            try
                            {
                                // 유닛과 아군 사이의 거리를 셀 단위로 계산
                                int distanceToAlly = WarhammerGeometryUtils.DistanceToInCells(
                                    unit.Position, unit.SizeRect,
                                    ally.Position, ally.SizeRect);

                                if (distanceToAlly <= 1)
                                {
                                    hasAllyInMeleeRange = true;
                                    Main.Log($"[CustomAI] BLOCKED {ability.Name}: Ally {ally.CharacterName} is within melee range ({distanceToAlly} cells)");
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Main.LogDebug($"[CustomAI] Error calculating distance to ally: {ex.Message}");
                            }
                        }
                    }

                    if (hasAllyInMeleeRange)
                    {
                        Main.Log($"[CustomAI] Skip {ability.Name}: RandomMeleeAoE ability blocked - allies in melee range would be hit!");
                        return true;
                    }
                    else
                    {
                        Main.Log($"[CustomAI] ALLOWED {ability.Name}: No allies in melee range, safe to use RandomMeleeAoE");
                    }
                }
            }

            // === 기존 일반 규칙 ===

            // 1. HP가 낮을 때 자해 스킬 차단 (규칙에 없는 경우 대비)
            // v2.1.1: 임계값을 40%로 상향, 한글 키워드 추가
            if (analysis.UnitHPPercent < 40f)
            {
                if (ContainsAny(name, "blood", "sacrifice", "reaper", "executioner",
                    "ensanguinate", "death cult", "self-harm", "cost hp", "oath",
                    "맹세", "피의", "희생"))
                {
                    Main.Log($"[CustomAI] HP cost ability BLOCKED by keyword: {ability.Name} (HP: {analysis.UnitHPPercent:F0}% < 40%)");
                    return true;
                }
            }

            // 2. 이미 활성화된 버프 스킵
            if (ability.Blueprint?.Range == AbilityRange.Personal)
            {
                try
                {
                    foreach (var buff in unit.Buffs.Enumerable)
                    {
                        // 로컬라이제이션 오류 방지 - 블루프린트 이름으로 비교
                        try
                        {
                            string buffBpName = buff.Blueprint?.name ?? "";
                            string abilityBpName = ability.Blueprint?.name ?? "";
                            if (!string.IsNullOrEmpty(buffBpName) && buffBpName == abilityBpName)
                                return true;
                        }
                        catch { /* 로컬라이제이션 오류 무시 */ }
                    }
                }
                catch { /* 버프 열거 오류 무시 */ }
            }

            // 3. 힐이 필요없을 때 힐 스킵 (효율성)
            var category = CategorizeAbility(ability);
            if (category == AbilityCategory.Heal && !analysis.TeamNeedsHealing && analysis.UnitHPPercent > 80f)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 최적의 행동 선택
        /// </summary>
        static (AbilityData, TargetWrapper) SelectBestAction(
            DecisionContext context, BaseUnitEntity unit, Settings.CharacterSettings settings,
            List<CategorizedAbility> abilities, CombatAnalysis analysis)
        {
            AbilityData bestAbility = null;
            TargetWrapper bestTarget = null;
            float bestScore = float.MinValue;

            // 디버그: 모든 어빌리티와 점수 출력
            var allScores = new List<(string name, string category, string target, float score, string blueprintName)>();

            foreach (var catAbility in abilities)
            {
                try
                {
                    var targets = GetPossibleTargets(context, catAbility.Ability, analysis);

                    // 타겟이 없는 경우도 로그
                    if (targets.Count == 0)
                    {
                        string bpName = catAbility.Ability?.Blueprint?.name ?? "?";
                        Main.LogDebug($"[CustomAI] {catAbility.Ability.Name} ({catAbility.Category}, BP:{bpName}): No valid targets");
                        continue;
                    }

                    foreach (var target in targets)
                    {
                        // Zone 스킬(Point 타겟)은 AOETargetSelector가 이미 검증함
                        string bpNameForCheck = catAbility.Ability?.Blueprint?.name?.ToLower() ?? "";
                        bool isZoneSkill = bpNameForCheck.Contains("keystone");

                        // Zone 스킬이 아닌 경우만 CanTarget 체크
                        if (!isZoneSkill && !catAbility.Ability.CanTarget(target))
                        {
                            // v2.0.8: CanTarget 실패 시 디버그 로그
                            var targetEntity = target.Entity as BaseUnitEntity;
                            if (targetEntity != null)
                            {
                                float distance = Vector3.Distance(unit.Position, targetEntity.Position);
                                int range = catAbility.Ability.Weapon?.Blueprint?.AttackRange ??
                                            catAbility.Ability.Blueprint?.GetRange() ?? 0;
                                Main.LogDebug($"[CustomAI] CanTarget FAILED: {catAbility.Ability.Name} -> {targetEntity.CharacterName}, " +
                                    $"Distance={distance:F1}, AbilityRange={range}");
                            }
                            continue;
                        }

                        float score = CalculateActionScore(
                            catAbility, target, unit, settings, analysis);

                        string bpName = catAbility.Ability?.Blueprint?.name ?? "?";
                        allScores.Add((catAbility.Ability.Name, catAbility.Category.ToString(), GetTargetName(target), score, bpName));

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestAbility = catAbility.Ability;
                            bestTarget = target;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Main.LogError($"[CustomAI] Error evaluating {catAbility.Ability.Name}: {ex}");
                }
            }

            // 디버그: 상위 5개 점수 출력
            var topScores = allScores.OrderByDescending(x => x.score).Take(5).ToList();
            foreach (var (name, category, target, score, bpName) in topScores)
            {
                Main.LogDebug($"[CustomAI] Score: {name} ({category}, BP:{bpName}) -> {target}: {score:F1}");
            }

            // === 최소 점수 임계값 체크 ===
            // 점수가 0 이하인 행동은 선택하지 않음 (아군 공격 등 방지)
            const float MIN_ACCEPTABLE_ACTION_SCORE = 0f;
            if (bestScore < MIN_ACCEPTABLE_ACTION_SCORE)
            {
                Main.Log($"[CustomAI] REJECTED: Best action has score {bestScore:F0} (below threshold {MIN_ACCEPTABLE_ACTION_SCORE}) - skipping");
                return (null, null);
            }

            return (bestAbility, bestTarget);
        }

        /// <summary>
        /// 행동 점수 계산 (역할 및 상황 기반)
        /// </summary>
        static float CalculateActionScore(
            CategorizedAbility catAbility, TargetWrapper target,
            BaseUnitEntity unit, Settings.CharacterSettings settings, CombatAnalysis analysis)
        {
            float score = SCORE_BASE;
            var ability = catAbility.Ability;
            var category = catAbility.Category;
            var targetEntity = target.Entity as BaseUnitEntity;

            if (targetEntity == null) return score;

            bool isEnemy = analysis.Enemies.Contains(targetEntity);
            bool isAlly = analysis.Allies.Contains(targetEntity) || targetEntity == unit;
            bool isSelf = targetEntity == unit;

            // === v2.0.9: PreferMelee 로직 - 이동은 SetupMoveCommand에서 처리 ===
            // 여기서는 원거리 공격에만 페널티 적용 (아래 ScoreAttack에서 처리됨)
            // 모든 능력을 차단하면 근접 공격/버프도 못 씀!

            // === 카테고리별 기본 점수 ===
            switch (category)
            {
                case AbilityCategory.Attack:
                    // 공격은 적에게만 - 아군 공격 절대 방지!
                    if (!isEnemy)
                    {
                        Main.LogDebug($"[CustomAI] BLOCKED: Attack on ally/self {targetEntity.CharacterName}");
                        return -999999f; // 아군 공격은 절대 불가 (DangerousAoE보다 더 낮게)
                    }

                    // === DangerousAoE 사전 체크 - ScoreAttack 호출 전에 차단 ===
                    // AoE 패턴에 아군이 있으면 이 함수에서 직접 반환 (score에 더해지지 않음)
                    // 추가: 한 번 차단되면 해당 턴 동안 이동해도 계속 차단 (이동 후 사용 방지)
                    if (ability != null && IsDangerousAoE(ability) && IsAoEAbility(ability) && settings.AvoidFriendlyFire)
                    {
                        string unitId = unit.UniqueId;
                        string abilityKey = $"{unitId}_{ability.Blueprint?.name ?? ability.Name}";
                        int currentRound = Game.Instance?.TurnController?.CombatRound ?? 0;

                        // 새 유닛 턴이면 차단 기록 초기화
                        if (_lastTurnUnitId != unitId)
                        {
                            _lastTurnUnitId = unitId;
                            // 이전 라운드의 차단 기록 정리
                            var keysToRemove = _blockedDangerousAoEThisTurn
                                .Where(kvp => kvp.Value < currentRound || !kvp.Key.StartsWith(unitId))
                                .Select(kvp => kvp.Key).ToList();
                            foreach (var key in keysToRemove)
                                _blockedDangerousAoEThisTurn.Remove(key);
                        }

                        // 이미 이번 턴에 차단된 능력이면 (이동해도) 계속 차단
                        if (_blockedDangerousAoEThisTurn.TryGetValue(abilityKey, out int blockedRound) && blockedRound == currentRound)
                        {
                            Main.Log($"[CustomAI] === DANGEROUS AOE TURN BLOCK ===: {ability.Name} -> {targetEntity.CharacterName} - " +
                                $"Already blocked this turn (move doesn't help)");
                            return float.MinValue;
                        }

                        var targetWrapper = new TargetWrapper(targetEntity);
                        var (patternAllies, patternEnemies, hitsSelf) = CountUnitsInActualPattern(ability, unit, targetWrapper, analysis);

                        if (patternAllies > 0 || hitsSelf)
                        {
                            // 차단 기록 저장
                            _blockedDangerousAoEThisTurn[abilityKey] = currentRound;

                            Main.Log($"[CustomAI] === DANGEROUS AOE ABSOLUTE BLOCK ===: {ability.Name} -> {targetEntity.CharacterName}, " +
                                $"Allies: {patternAllies}, Self: {hitsSelf} - Returning float.MinValue (blocked for this turn)");
                            return float.MinValue;
                        }
                    }

                    score += ScoreAttack(targetEntity, unit, settings, analysis, ability);
                    break;

                case AbilityCategory.Heal:
                    // 힐은 아군에게만
                    if (isEnemy)
                    {
                        Main.LogDebug($"[CustomAI] BLOCKED: Heal on enemy {targetEntity.CharacterName}");
                        return -9999f;
                    }
                    score += ScoreHeal(targetEntity, unit, analysis, isSelf);
                    break;

                case AbilityCategory.Buff:
                    // 버프는 아군에게만
                    if (isEnemy)
                    {
                        Main.LogDebug($"[CustomAI] BLOCKED: Buff on enemy {targetEntity.CharacterName}");
                        return -9999f;
                    }
                    score += ScoreBuff(targetEntity, unit, analysis, isSelf, ability);
                    break;

                case AbilityCategory.Debuff:
                    // 디버프는 적에게만
                    if (!isEnemy)
                    {
                        Main.LogDebug($"[CustomAI] BLOCKED: Debuff on ally/self {targetEntity.CharacterName}");
                        return -9999f;
                    }
                    score += ScoreDebuff(targetEntity, analysis);
                    break;

                case AbilityCategory.Defense:
                    // 방어는 아군에게만
                    if (isEnemy)
                    {
                        Main.LogDebug($"[CustomAI] BLOCKED: Defense on enemy {targetEntity.CharacterName}");
                        return -9999f;
                    }
                    score += ScoreDefense(targetEntity, unit, analysis, isSelf);
                    break;

                case AbilityCategory.Taunt:
                    // 도발은 자신 (AoE) - 탱커 우선
                    if (!isSelf)
                    {
                        return -9999f;
                    }
                    score += ScoreTaunt(unit, analysis);
                    break;

                case AbilityCategory.Exploit:
                    // Exploit은 적에게만 (공격 변형)
                    if (!isEnemy)
                    {
                        return -9999f;
                    }
                    score += ScoreExploit(targetEntity, analysis);
                    break;

                case AbilityCategory.Movement:
                    // 이동은 역할에 따라 방향 결정
                    score += ScoreMovement(unit, targetEntity, settings, analysis);
                    break;

                case AbilityCategory.Utility:
                    // 유틸리티는 상황에 따라
                    score += 10f;
                    break;
            }

            // === 역할 기반 보정 ===
            score += GetRoleBonus(category, analysis.UnitRole, analysis);

            // === Range Preference 보정 ===
            if (category == AbilityCategory.Attack && isEnemy)
            {
                score += GetRangePreferenceBonus(ability, unit, targetEntity, settings, analysis);
            }

            // === AP 효율성 ===
            if (catAbility.APCost > 0)
            {
                score += SCORE_AP_EFFICIENCY / catAbility.APCost;
            }

            // === 거리 패널티 (RangePreference에 따라 조정) ===
            if (targetEntity != unit)
            {
                float distance = Vector3.Distance(unit.Position, targetEntity.Position);
                float distancePenalty = SCORE_DISTANCE_PENALTY;

                // 근접 선호 시 거리 패널티 강화 (멀리 있는 적 공격 비선호)
                if (settings.RangePreference == Settings.RangePreference.PreferMelee)
                {
                    distancePenalty *= 2f;
                }
                // 원거리 선호 시 거리 패널티 감소
                else if (settings.RangePreference == Settings.RangePreference.PreferRanged)
                {
                    distancePenalty *= 0.5f;
                }

                score -= distance * distancePenalty;
            }

            // === 스킬별 특수 규칙 보너스 ===
            string blueprintName = ability.Blueprint?.name ?? "";
            var rule = GetAbilityRule(blueprintName);
            if (rule != null)
            {
                float targetHP = isEnemy ? GetHPPercent(targetEntity) : 100f;

                switch (rule.Type)
                {
                    case AbilityRuleType.Finisher:
                        // 마무리 스킬: 적 HP가 낮을수록 보너스
                        if (isEnemy && targetHP < 50f)
                        {
                            score += 80f + (50f - targetHP) * 2f; // HP 10%면 +160
                        }
                        else if (isEnemy && targetHP >= 50f)
                        {
                            // 적 HP가 높으면 페널티 (마무리용 스킬을 먼저 쓰지 않도록)
                            score -= 50f;
                        }
                        break;

                    case AbilityRuleType.PreAttackBuff:
                        // 공격 전 버프: Opening 단계에서 보너스
                        if (analysis.Phase == CombatPhase.Opening)
                        {
                            score += 70f;
                        }
                        else
                        {
                            // Opening이 아닌 단계에서는 약간의 보너스만
                            score += 20f;
                        }
                        break;

                    case AbilityRuleType.SelfDamage:
                        // 자해 스킬: HP가 높을수록 더 안전하므로 보너스
                        float hpSafety = (analysis.UnitHPPercent - rule.MinHPPercent) / 100f;
                        score += hpSafety * 30f; // HP 여유가 있으면 보너스
                        break;

                    case AbilityRuleType.StackingBuff:
                        // 중첩 버프: 항상 유지하는게 좋음
                        score += 25f;

                        // === 구역 스킬 (Keystone) 차별화 ===
                        string bpLower = blueprintName.ToLower();
                        if (bpLower.Contains("keystone"))
                        {
                            var zoneScore = ScoreKeystoneZone(bpLower, unit, analysis);
                            score += zoneScore;
                            Main.LogDebug($"[CustomAI] Zone skill {blueprintName}: +{zoneScore:F1} bonus");
                        }
                        break;
                }
            }

            return score;
        }

        /// <summary>
        /// 구역 스킬 (Keystone) 점수 계산
        /// - Frontline: 아군이 적에게 가까울수록 보너스
        /// - Backline: 중간 거리 아군에게 보너스
        /// - Rear: 아군이 적에게서 멀수록 보너스
        /// </summary>
        static float ScoreKeystoneZone(string blueprintName, BaseUnitEntity caster, CombatAnalysis analysis)
        {
            float score = 0f;

            try
            {
                // 적들의 평균 위치 계산
                Vector3 enemyCenter = Vector3.zero;
                int enemyCount = 0;
                foreach (var enemy in analysis.Enemies)
                {
                    if (enemy == null) continue;
                    enemyCenter += enemy.Position;
                    enemyCount++;
                }
                if (enemyCount == 0) return 0f;
                enemyCenter /= enemyCount;

                // 아군들의 적과의 거리 분류
                List<(BaseUnitEntity ally, float distance)> allyDistances = new();
                foreach (var ally in analysis.Allies)
                {
                    if (ally == null) continue;
                    float dist = Vector3.Distance(ally.Position, enemyCenter);
                    allyDistances.Add((ally, dist));
                }
                // 캐스터 본인도 포함
                allyDistances.Add((caster, Vector3.Distance(caster.Position, enemyCenter)));

                if (allyDistances.Count == 0) return 0f;

                // 거리로 정렬
                allyDistances.Sort((a, b) => a.distance.CompareTo(b.distance));

                // 전방/중간/후방 분류 (3등분)
                int third = Math.Max(1, allyDistances.Count / 3);
                int frontCount = 0, midCount = 0, rearCount = 0;

                for (int i = 0; i < allyDistances.Count; i++)
                {
                    if (i < third) frontCount++;
                    else if (i < third * 2) midCount++;
                    else rearCount++;
                }

                // 스킬별 점수
                if (blueprintName.Contains("frontline"))
                {
                    // 전방에 아군이 많을수록 보너스
                    score = frontCount * 30f;
                    Main.LogDebug($"[CustomAI] Frontline zone: {frontCount} allies in front");
                }
                else if (blueprintName.Contains("backline"))
                {
                    // 중간에 아군이 많을수록 보너스
                    score = midCount * 30f;
                    Main.LogDebug($"[CustomAI] Backline zone: {midCount} allies in middle");
                }
                else if (blueprintName.Contains("rear"))
                {
                    // 후방에 아군이 많을수록 보너스
                    score = rearCount * 30f;
                    Main.LogDebug($"[CustomAI] Rear zone: {rearCount} allies in rear");
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] ScoreKeystoneZone error: {ex.Message}");
            }

            return score;
        }

        /// <summary>
        /// Zone 스킬 최적 위치 찾기 (겹침 체크 포함)
        /// - 기존 AreaEffects와 겹치지 않는 위치 찾기
        /// - 아군을 가장 많이 포함하는 위치 선택
        /// </summary>
        // 평가 중 예약된 Zone 위치 추적 (같은 턴에 여러 Zone이 같은 위치 선택 방지)
        static List<Vector3> s_PendingZonePositions = new List<Vector3>();
        static string s_LastZoneEvalUnit = "";
        static int s_ZoneEvalCount = 0;

        static TargetWrapper FindBestZonePosition(DecisionContext context, AbilityData ability, CombatAnalysis analysis)
        {
            try
            {
                var caster = context.Unit;
                var casterNode = caster.GetNearestNodeXZ() as CustomGridNodeBase;
                if (casterNode == null) return null;

                // 새 유닛이면 예약 위치 초기화
                string unitId = caster.UniqueId;
                if (s_LastZoneEvalUnit != unitId)
                {
                    s_PendingZonePositions.Clear();
                    s_LastZoneEvalUnit = unitId;
                    s_ZoneEvalCount = 0;
                    Main.LogDebug($"[CustomAI] Zone evaluation started for {caster.CharacterName}, cleared pending positions");
                }

                // Zone 스킬은 최대 3개, 예약이 3개 초과면 새 평가 사이클로 간주
                s_ZoneEvalCount++;
                if (s_ZoneEvalCount > 3)
                {
                    s_PendingZonePositions.Clear();
                    s_ZoneEvalCount = 1;
                    Main.LogDebug($"[CustomAI] Zone evaluation cycle reset (count exceeded 3)");
                }

                // AbilityInfo로 패턴 정보 가져오기
                var abilityInfo = new AbilityInfo(ability);
                if (abilityInfo.pattern == null)
                {
                    Main.LogDebug($"[CustomAI] Zone {ability.Name}: no pattern found");
                    return null;
                }

                // 기존 Strategist Zone들 수집
                var existingZones = new List<Kingmaker.EntitySystem.Entities.AreaEffectEntity>();
                foreach (var areaEffect in Game.Instance.State.AreaEffects)
                {
                    if (areaEffect.Blueprint.IsStrategistAbility)
                    {
                        existingZones.Add(areaEffect);
                    }
                }

                Main.LogDebug($"[CustomAI] Zone {ability.Name}: {existingZones.Count} existing strategist zones, {s_PendingZonePositions.Count} pending");

                // 아군 위치들을 기준으로 가능한 배치 위치 탐색
                TargetWrapper bestTarget = null;
                float bestScore = float.MinValue;
                int bestAllyCount = 0;

                // 디버그용 카운터
                int positionsChecked = 0;
                int rejectedByRange = 0;
                int rejectedByOverlapExisting = 0;
                int rejectedByOverlapPending = 0;
                int rejectedByNoAllies = 0;
                int validPositions = 0;

                foreach (var ally in analysis.Allies)
                {
                    if (ally == null) continue;

                    var allyNode = ally.GetNearestNodeXZ() as CustomGridNodeBase;
                    if (allyNode == null) continue;

                    // 아군 위치 주변 검사 (패턴 범위 내)
                    var graph = casterNode.Graph as CustomGridGraph;
                    if (graph == null) continue;

                    // 패턴 범위만큼 오프셋하여 검사 (Zone 크기 고려하여 넓게 검색)
                    for (int dx = -8; dx <= 8; dx++)
                    {
                        for (int dz = -8; dz <= 8; dz++)
                        {
                            var checkNode = graph.GetNode(
                                allyNode.XCoordinateInGrid + dx,
                                allyNode.ZCoordinateInGrid + dz
                            );

                            if (checkNode == null || !checkNode.Walkable) continue;

                            positionsChecked++;

                            // 사거리 체크
                            float distance = Vector3.Distance(casterNode.Vector3Position, checkNode.Vector3Position);
                            if (distance > abilityInfo.maxRange || distance < abilityInfo.minRange)
                            {
                                rejectedByRange++;
                                continue;
                            }

                            // LOS 체크 - 캐스터가 해당 위치를 볼 수 있는지 확인
                            if (!LosCalculations.HasLos(casterNode, default(IntRect), checkNode, default(IntRect)))
                            {
                                continue; // 시야 없으면 스킵
                            }

                            // 겹침 체크 - 기존 Zone과 예약된 위치 모두 확인
                            bool overlaps = false;

                            // 기존 배치된 Zone 체크 - 중심 간 거리로 겹침 판단
                            // 6.5f: 겹침 방지 + 충분한 유효 위치 확보 균형
                            const float ZONE_OVERLAP_DISTANCE = 6.5f;
                            bool existingOverlap = false;
                            foreach (var existingZone in existingZones)
                            {
                                float distToZone = Vector3.Distance(existingZone.Position, checkNode.Vector3Position);
                                if (distToZone < ZONE_OVERLAP_DISTANCE)
                                {
                                    existingOverlap = true;
                                    overlaps = true;
                                    break;
                                }
                            }
                            if (existingOverlap)
                            {
                                rejectedByOverlapExisting++;
                                continue;
                            }

                            // 이번 평가에서 이미 예약된 위치 체크 (같은 거리 기준)
                            foreach (var pendingPos in s_PendingZonePositions)
                            {
                                if (Vector3.Distance(pendingPos, checkNode.Vector3Position) < ZONE_OVERLAP_DISTANCE)
                                {
                                    overlaps = true;
                                    break;
                                }
                            }
                            if (overlaps)
                            {
                                rejectedByOverlapPending++;
                                continue;
                            }

                            // 이 위치에서 패턴이 포함하는 아군 수 계산
                            int allyCount = 0;
                            float positionScore = 0f;

                            foreach (var checkAlly in analysis.Allies)
                            {
                                if (checkAlly == null) continue;
                                float distToCenter = Vector3.Distance(checkAlly.Position, checkNode.Vector3Position);

                                // 패턴 반경 내에 있는지 (대략적으로 3셀 범위)
                                if (distToCenter <= 4.5f) // 약 3셀
                                {
                                    allyCount++;
                                    positionScore += 100f - distToCenter * 10f; // 중심에 가까울수록 보너스
                                }
                            }

                            if (allyCount > 0)
                            {
                                validPositions++;
                                if (allyCount > bestAllyCount ||
                                    (allyCount == bestAllyCount && positionScore > bestScore))
                                {
                                    bestAllyCount = allyCount;
                                    bestScore = positionScore;
                                    bestTarget = new TargetWrapper(checkNode.Vector3Position);
                                }
                            }
                            else
                            {
                                rejectedByNoAllies++;
                            }
                        }
                    }
                }

                // 상세 디버그 로그
                Main.LogDebug($"[CustomAI] Zone {ability.Name} search: checked={positionsChecked}, range={rejectedByRange}, existZone={rejectedByOverlapExisting}, pendZone={rejectedByOverlapPending}, noAllies={rejectedByNoAllies}, valid={validPositions}");

                if (bestTarget != null)
                {
                    // 선택된 위치를 예약 목록에 추가
                    s_PendingZonePositions.Add(bestTarget.Point);
                    Main.LogDebug($"[CustomAI] Zone {ability.Name}: found position covering {bestAllyCount} allies, score={bestScore:F1}, reserved at {bestTarget.Point}");
                    Main.LogDebug($"[CustomAI] Zone pending positions: {s_PendingZonePositions.Count} reserved");
                }
                else
                {
                    Main.LogDebug($"[CustomAI] Zone skill {ability.Name}: FAILED - no valid position found");
                }

                return bestTarget;
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] FindBestZonePosition error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Range Preference에 따른 공격 점수 보정 (프로필 기반 적응형)
        /// </summary>
        static float GetRangePreferenceBonus(AbilityData ability, BaseUnitEntity unit,
            BaseUnitEntity target, Settings.CharacterSettings settings, CombatAnalysis analysis)
        {
            float bonus = 0f;
            bool isMeleeAbility = IsMeleeAbility(ability);
            bool isRangedAbility = IsRangedAbility(ability);
            float distanceToTarget = Vector3.Distance(unit.Position, target.Position);

            // 근접 범위 판정 (약 2.5 유닛 이내)
            bool inMeleeRange = distanceToTarget <= 3.0f;

            // 프로필 기반 적응: 캐릭터가 가진 스킬에 맞게 조정
            var profile = analysis.Profile;
            bool hasNoMeleeAttacks = profile != null && profile.MeleeAttacks == 0;
            bool hasNoRangedAttacks = profile != null && profile.RangedAttacks == 0;

            switch (settings.RangePreference)
            {
                case Settings.RangePreference.PreferMelee:
                    // 근접 공격이 없으면 원거리 페널티 면제
                    if (hasNoMeleeAttacks)
                    {
                        // 근접 공격이 없으니 원거리로 싸워야 함
                        if (isRangedAbility) bonus += 20f;
                    }
                    else
                    {
                        // 근접 공격 대폭 우선
                        if (isMeleeAbility)
                        {
                            bonus += 60f;
                            // 이미 근접 범위면 추가 보너스
                            if (inMeleeRange) bonus += 30f;
                        }
                        // 원거리 공격 페널티 (근접 범위 내가 아닐 때만)
                        else if (isRangedAbility)
                        {
                            if (inMeleeRange)
                            {
                                // 근접 범위 내에서는 원거리도 허용 (적당한 페널티)
                                bonus -= 20f;
                            }
                            else
                            {
                                // v2.0.11: 근접 범위에 적이 있는지 확인
                                // 근접 범위에 적이 없으면 원거리 공격 허용 (이동이 불가능할 수 있음)
                                bool anyEnemyInMeleeRange = false;
                                if (analysis?.Enemies != null)
                                {
                                    foreach (var enemy in analysis.Enemies)
                                    {
                                        if (enemy != null && Vector3.Distance(unit.Position, enemy.Position) <= 3.0f)
                                        {
                                            anyEnemyInMeleeRange = true;
                                            break;
                                        }
                                    }
                                }

                                if (anyEnemyInMeleeRange)
                                {
                                    // 근접 범위에 적이 있으니 근접 공격 우선 - 원거리 차단
                                    bonus -= 500f;
                                    Main.LogDebug($"[CustomAI] PreferMelee: BLOCKING ranged (enemy in melee range, attack melee first)");
                                }
                                else
                                {
                                    // 근접 범위에 적이 없음 - 원거리 공격 허용 (낮은 페널티만)
                                    // Tank 등 이동 안 하는 역할도 공격 가능하도록
                                    bonus -= 30f;
                                    Main.LogDebug($"[CustomAI] PreferMelee: Allowing ranged (no enemy in melee range)");
                                }
                            }
                        }
                    }
                    break;

                case Settings.RangePreference.PreferRanged:
                    // 원거리 공격이 없으면 근접 페널티 면제
                    if (hasNoRangedAttacks)
                    {
                        if (isMeleeAbility) bonus += 20f;
                    }
                    else
                    {
                        // 원거리 공격 우선
                        if (isRangedAbility)
                        {
                            bonus += 50f;
                            // 안전 거리면 추가 보너스
                            if (distanceToTarget > 5f) bonus += 20f;
                        }
                        // 근접 공격은 어쩔 수 없을 때만
                        else if (isMeleeAbility)
                        {
                            bonus -= 30f;
                        }
                    }
                    break;

                case Settings.RangePreference.MaintainRange:
                    // 현재 장착 무기 범위에 맞는 공격 선호
                    if (isRangedAbility && distanceToTarget > 3f)
                        bonus += 30f;
                    else if (isMeleeAbility && inMeleeRange)
                        bonus += 30f;
                    break;

                case Settings.RangePreference.Adaptive:
                default:
                    // 현재 위치에서 바로 쓸 수 있는 것 선호
                    if (isMeleeAbility && inMeleeRange)
                        bonus += 20f;
                    else if (isRangedAbility && !inMeleeRange)
                        bonus += 20f;
                    break;
            }

            return bonus;
        }

        /// <summary>
        /// 근접 어빌리티인지 판정
        /// </summary>
        static bool IsMeleeAbility(AbilityData ability)
        {
            if (ability?.Blueprint == null) return false;

            // 블루프린트 이름(영문) + 로컬 이름 모두 검사
            string blueprintName = ability.Blueprint.name?.ToLower() ?? "";
            string localName = ability.Name?.ToLower() ?? "";
            string combinedName = blueprintName + " " + localName;

            // 키워드 기반 판정 - 블루프린트 이름에서 "melee" 확인
            if (ContainsAny(combinedName, "melee", "strike", "slash", "stab", "punch", "kick",
                "swing", "cleave", "charge", "bash", "smash"))
                return true;

            // Range가 Touch면 근접
            var range = ability.Blueprint.Range;
            if (range == AbilityRange.Touch)
                return true;

            // Weapon range는 무기에 따라 다름
            if (range == AbilityRange.Weapon && ability.SourceItem != null)
            {
                // 무기 블루프린트 이름 사용
                var weaponBlueprintName = ability.SourceItem.Blueprint?.name?.ToLower() ?? "";
                var weaponLocalName = ability.SourceItem.Name?.ToLower() ?? "";
                string weaponName = weaponBlueprintName + " " + weaponLocalName;

                if (ContainsAny(weaponName, "sword", "axe", "hammer", "mace", "blade",
                    "knife", "dagger", "staff", "chainsword", "power fist", "power_fist"))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 원거리 어빌리티인지 판정
        /// </summary>
        static bool IsRangedAbility(AbilityData ability)
        {
            if (ability?.Blueprint == null) return false;

            // 블루프린트 이름(영문) + 로컬 이름 모두 검사
            string blueprintName = ability.Blueprint.name?.ToLower() ?? "";
            string localName = ability.Name?.ToLower() ?? "";
            string combinedName = blueprintName + " " + localName;

            // 키워드 기반 판정 - "Shotgun", "Scatter" 등 확인
            if (ContainsAny(combinedName, "shot", "shoot", "fire", "bolt", "blast", "ray", "beam",
                "rifle", "pistol", "gun", "scatter", "snipe", "ranged", "shotgun"))
                return true;

            // Weapon range - 무기 이름으로 판정
            var range = ability.Blueprint.Range;
            if (range == AbilityRange.Weapon && ability.SourceItem != null)
            {
                var weaponBlueprintName = ability.SourceItem.Blueprint?.name?.ToLower() ?? "";
                var weaponLocalName = ability.SourceItem.Name?.ToLower() ?? "";
                string weaponName = weaponBlueprintName + " " + weaponLocalName;

                if (ContainsAny(weaponName, "rifle", "pistol", "gun", "las", "bolt", "plasma",
                    "shotgun", "autogun", "stubber", "foehammer"))
                    return true;
            }

            // Personal이 아닌 경우 원거리일 가능성 (Touch/Weapon 제외)
            if (range != AbilityRange.Personal && range != AbilityRange.Touch && range != AbilityRange.Weapon)
                return true;

            return false;
        }

        /// <summary>
        /// AoE 어빌리티인지 판정
        /// v2.0.10: 디컴파일 소스 분석을 통해 위험한 컴포넌트 타입 감지 추가
        /// </summary>
        static bool IsAoEAbility(AbilityData ability)
        {
            if (ability?.Blueprint == null) return false;

            try
            {
                // 1. 게임의 IsAOE 프로퍼티 확인
                if (ability.IsAOE) return true;

                // 2. PatternSettings 확인 (Circle, Cone, Ray, Sector 등)
                var patternSettings = ability.GetPatternSettings();
                if (patternSettings != null) return true;

                // 3. v2.0.10: 위험한 컴포넌트 타입 체크
                // 디컴파일 소스 분석 결과 이 컴포넌트들은 TargetType.Any를 사용하거나
                // 진영 체크 없이 다중 타겟을 공격함
                var components = ability.Blueprint.ComponentsArray;
                if (components != null)
                {
                    foreach (var comp in components)
                    {
                        if (comp == null) continue;
                        string typeName = comp.GetType().Name;

                        // 위험한 다중 타겟 컴포넌트들:
                        // - ScatterPattern: TargetType.Any 하드코딩됨
                        // - AbilityMeleeBurst: IsAoe=true일 때 모든 유닛 공격
                        // - AbilityCustomRam: TargetType.Any 하드코딩됨
                        // - AbilityCustomDirectMovement: 경로상 모든 유닛 공격 가능
                        // - AbilityStepThroughTarget: TargetType.Any 하드코딩됨
                        // - AbilityCustomBladeDance: 진영 체크 없음
                        // - AbilityDeliverChain: 체인 공격
                        // - AbilityTargetsAround: 범위 내 모든 유닛
                        // - WarhammerAbilityAttackDelivery: Burst/Scatter 모드
                        if (typeName.Contains("Scatter") ||
                            typeName.Contains("MeleeBurst") ||
                            typeName.Contains("CustomRam") ||
                            typeName.Contains("DirectMovement") ||
                            typeName.Contains("StepThrough") ||
                            typeName.Contains("BladeDance") ||
                            typeName.Contains("DeliverChain") ||
                            typeName.Contains("TargetsAround") ||
                            typeName.Contains("TargetsInPattern") ||
                            typeName.Contains("AttackDelivery"))
                        {
                            Main.LogDebug($"[CustomAI] Detected dangerous component: {typeName} in {ability.Name}");
                            return true;
                        }
                    }
                }

                // 4. 블루프린트 이름 키워드 확인
                string blueprintName = ability.Blueprint.name?.ToLower() ?? "";
                string localName = ability.Name?.ToLower() ?? "";
                string combinedName = blueprintName + " " + localName;

                if (ContainsAny(combinedName, "burst", "scatter", "cone", "grenade", "aoe",
                    "area", "explosion", "scream", "shriek", "nova", "wave", "blast area",
                    "ultimate", "궁극기", "실체",
                    "응시", "일제사격", "폭발", "광역"))
                    return true;
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] IsAoEAbility error: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// AOE/Cone 패턴 내 아군 체크 (실제 패턴 사용)
        /// 게임의 AOETargetSelector와 동일한 방식으로 패턴 데이터를 사용
        /// </summary>
        static (int alliesHit, int enemiesHit, bool hitsSelf) CountUnitsInActualPattern(
            AbilityData ability, BaseUnitEntity caster, TargetWrapper target, CombatAnalysis analysis)
        {
            int alliesHit = 0;
            int enemiesHit = 0;
            bool hitsSelf = false;

            try
            {
                Main.LogDebug($"[CustomAI] Pattern check starting for {ability.Name}");

                // AbilityData.GetPattern() 사용 - 내부적으로 패턴 설정 확인
                var orientedPattern = ability.GetPattern(target, caster.Position);

                // 패턴이 비어있으면 폴백
                int nodeCount = 0;
                foreach (var n in orientedPattern.Nodes) nodeCount++;
                Main.LogDebug($"[CustomAI] Pattern nodes: {nodeCount}");

                if (nodeCount == 0)
                {
                    Main.LogDebug($"[CustomAI] Pattern check: no pattern for {ability.Name}");
                    return (-1, -1, false); // -1은 패턴 없음을 의미
                }

                // 패턴 내 모든 노드 확인
                foreach (var node in orientedPattern.Nodes)
                {
                    if (node.TryGetUnit(out var unitInPattern))
                    {
                        if (unitInPattern == caster)
                        {
                            hitsSelf = true;
                        }
                        else if (analysis.Allies.Contains(unitInPattern))
                        {
                            alliesHit++;
                            Main.LogDebug($"[CustomAI] Pattern ALLY HIT: {unitInPattern.CharacterName}");
                        }
                        else if (analysis.Enemies.Contains(unitInPattern))
                        {
                            enemiesHit++;
                        }
                    }
                }

                Main.LogDebug($"[CustomAI] Pattern check for {ability.Name}: Allies={alliesHit}, Enemies={enemiesHit}, Self={hitsSelf}");
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] CountUnitsInActualPattern error: {ex.Message}");
                return (-1, -1, false);
            }

            return (alliesHit, enemiesHit, hitsSelf);
        }

        /// <summary>
        /// 타겟 위치 근처의 아군 수 계산 (AoE 피해 범위 내)
        /// </summary>
        static int CountAlliesNearTarget(BaseUnitEntity target, BaseUnitEntity caster,
            CombatAnalysis analysis, float radius = 5f)
        {
            int allyCount = 0;
            Vector3 targetPos = target.Position;

            try
            {
                // 모든 아군 확인 (본인 제외)
                foreach (var ally in analysis.Allies)
                {
                    if (ally == null || ally == caster) continue;

                    float distance = Vector3.Distance(targetPos, ally.Position);
                    if (distance <= radius)
                    {
                        allyCount++;
                    }
                }

                // 캐스터 본인도 확인 (자폭 방지)
                float casterDistance = Vector3.Distance(targetPos, caster.Position);
                if (casterDistance <= radius)
                {
                    allyCount++;
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] CountAlliesNearTarget error: {ex.Message}");
            }

            return allyCount;
        }

        /// <summary>
        /// 타겟 위치 근처의 적 수 계산 (AoE 효과 범위 내)
        /// </summary>
        static int CountEnemiesNearTarget(BaseUnitEntity target, CombatAnalysis analysis, float radius = 5f)
        {
            int enemyCount = 0;
            Vector3 targetPos = target.Position;

            try
            {
                foreach (var enemy in analysis.Enemies)
                {
                    if (enemy == null) continue;

                    float distance = Vector3.Distance(targetPos, enemy.Position);
                    if (distance <= radius)
                    {
                        enemyCount++;
                    }
                }
            }
            catch { /* 무시 */ }

            return enemyCount;
        }

        /// <summary>
        /// 공격 점수 계산
        /// </summary>
        static float ScoreAttack(BaseUnitEntity target, BaseUnitEntity unit,
            Settings.CharacterSettings settings, CombatAnalysis analysis,
            AbilityData ability = null)
        {
            float score = 0f;
            float targetHP = GetHPPercent(target);

            // 마무리 보너스 (낮은 HP 적)
            if (settings.FinishLowHPEnemies && targetHP < 30f)
            {
                score += SCORE_FINISH_BONUS;
            }
            else
            {
                // 약한 적 선호
                score += (100f - targetHP) * 0.3f;
            }

            // 집중 공격 보너스 (팀이 같은 타겟 공격)
            if (target == analysis.FocusTarget)
            {
                score += SCORE_FOCUS_FIRE_BONUS;
            }

            // 근접 교전 중이면 교전 중인 적 우선
            if (analysis.IsEngaged)
            {
                try
                {
                    var engagedByUnits = unit.GetEngagedByUnits();
                    if (engagedByUnits != null && engagedByUnits.Contains(target))
                    {
                        score += 30f;
                    }
                }
                catch { /* 무시 */ }
            }

            // === AoE Friendly Fire 페널티 (패턴 기반) ===
            bool isAoE = IsAoEAbility(ability);
            Main.LogDebug($"[CustomAI] AoE check: {ability?.Name} - IsAoE={isAoE}, AvoidFF={settings.AvoidFriendlyFire}");

            if (ability != null && settings.AvoidFriendlyFire && isAoE)
            {
                // 실제 패턴으로 충돌 체크 시도
                var targetWrapper = new TargetWrapper(target);
                var (patternAllies, patternEnemies, hitsSelf) = CountUnitsInActualPattern(ability, unit, targetWrapper, analysis);

                int alliesHit;
                int enemiesHit;

                if (patternAllies >= 0) // 패턴 체크 성공
                {
                    alliesHit = patternAllies;
                    enemiesHit = patternEnemies;

                    // 자기 자신 히트도 페널티
                    if (hitsSelf)
                    {
                        alliesHit++;
                        Main.Log($"[CustomAI] AoE SELF HIT: {ability.Name} would hit caster!");
                    }
                }
                else // 패턴 없음 - 기존 방식으로 폴백
                {
                    Main.LogDebug($"[CustomAI] Pattern fallback for {ability.Name}: using radius check");
                    alliesHit = CountAlliesNearTarget(target, unit, analysis, 4f);
                    enemiesHit = CountEnemiesNearTarget(target, analysis, 4f);
                    Main.LogDebug($"[CustomAI] Radius check: {alliesHit} allies, {enemiesHit} enemies near {target.CharacterName}");
                }

                if (alliesHit > 0)
                {
                    // === 위험한 광역 스킬(DangerousAoE)은 아군 1명이라도 있으면 절대 금지 ===
                    bool isDangerousAoE = IsDangerousAoE(ability);
                    if (isDangerousAoE)
                    {
                        Main.Log($"[CustomAI] *** DANGEROUS AOE HARD BLOCK ***: {ability.Name} -> {target.CharacterName}, " +
                            $"Allies in pattern: {alliesHit} - ABILITY COMPLETELY BLOCKED (DangerousAoE rule)");
                        return -99999f; // 절대적으로 낮은 점수 - 이 능력은 절대 선택되지 않음
                    }

                    // 아군이 맞으면 강한 페널티 (게임 AI와 동일)
                    float penalty = alliesHit * 500f; // 아군 1명당 -500 (매우 높은 페널티)
                    score -= penalty;
                    Main.Log($"[CustomAI] AoE Friendly Fire BLOCKED: {ability.Name} -> {target.CharacterName}, " +
                        $"Allies in pattern: {alliesHit}, Enemies in pattern: {enemiesHit}, Penalty: -{penalty:F0}");

                    // 아군이 적보다 많으면 사실상 사용 불가
                    if (alliesHit >= enemiesHit)
                    {
                        score -= 5000f; // 추가 페널티로 확실히 차단
                    }
                }

                // MinEnemiesForAoE 체크 - 최소 적 수 미달 시 페널티
                if (enemiesHit < settings.MinEnemiesForAoE)
                {
                    score -= 50f;
                    Main.LogDebug($"[CustomAI] AoE not worth it: only {enemiesHit} enemies (min: {settings.MinEnemiesForAoE})");
                }
                else
                {
                    // 적이 많을수록 보너스
                    score += (enemiesHit - 1) * 20f;
                }
            }

            // === 위협도 기반 타겟 우선순위 ===
            // 고위협 적(데미지 딜러)을 우선 처리
            if (analysis.EnemyThreatScores.TryGetValue(target, out float threatScore))
            {
                // 위협도가 높은 적 우선 공격
                score += threatScore * 0.5f;

                // 가장 위험한 적이면 추가 보너스
                if (target == analysis.HighestThreatEnemy)
                {
                    score += 25f;
                    Main.LogDebug($"[ThreatTarget] Prioritizing highest threat: {target.CharacterName} (threat: {threatScore:F1})");
                }
            }

            // 우리를 타겟팅 중인 적 우선 (자기 방어)
            try
            {
                if (target.CombatState?.ManualTarget == unit)
                {
                    score += 20f;
                }
            }
            catch { }

            // === 원거리 공격 명중률 체크 ===
            // 명중률이 낮으면 페널티를 주어 이동을 유도
            if (ability != null && IsRangedAbility(ability))
            {
                float hitChance = EstimateHitChance(unit, target, ability);

                if (hitChance < MIN_ACCEPTABLE_HIT_CHANCE)
                {
                    // 명중률이 60% 미만이면 페널티 (0%에서 최대, 60%에서 0)
                    float hitPenalty = LOW_HIT_CHANCE_PENALTY * (1f - hitChance / MIN_ACCEPTABLE_HIT_CHANCE);
                    score -= hitPenalty;
                    Main.Log($"[HitChance] LOW HIT CHANCE PENALTY: {ability.Name} -> {target.CharacterName}, " +
                        $"HitChance: {hitChance:F0}%, Penalty: -{hitPenalty:F0} (should reposition)");
                }
            }

            return score;
        }

        /// <summary>
        /// 힐 점수 계산
        /// </summary>
        static float ScoreHeal(BaseUnitEntity target, BaseUnitEntity unit,
            CombatAnalysis analysis, bool isSelf)
        {
            float score = 0f;
            float targetHP = GetHPPercent(target);

            // 위급한 아군 최우선
            if (targetHP < 30f)
            {
                score += SCORE_HEAL_URGENT;
                score += (30f - targetHP) * 2f; // 더 위급할수록 높은 점수
            }
            else if (targetHP < 70f)
            {
                score += SCORE_HEAL_NORMAL;
                score += (70f - targetHP);
            }
            else
            {
                // HP가 충분하면 힐 점수 낮춤
                score -= 50f;
            }

            // 가장 약한 아군이면 추가 보너스
            if (target == analysis.WeakestAlly)
            {
                score += 30f;
            }

            // 자신이 위급하면 자가 치유 우선
            if (isSelf && analysis.UnitHPPercent < 40f)
            {
                score += 40f;
            }

            return score;
        }

        /// <summary>
        /// 버프 점수 계산 - 역할 기반 스마트 버프 대상 선택
        /// </summary>
        static float ScoreBuff(BaseUnitEntity target, BaseUnitEntity unit,
            CombatAnalysis analysis, bool isSelf, AbilityData ability = null)
        {
            float score = 0f;

            // 전투 시작 단계에서 버프 우선
            if (analysis.Phase == CombatPhase.Opening)
            {
                score += SCORE_BUFF_OPENING;
            }
            else
            {
                score += SCORE_BUFF_NORMAL;
            }

            // 위급 상황에서는 버프보다 다른 행동 우선
            if (analysis.CriticalAlliesCount > 0)
            {
                score -= 30f;
            }

            // === 역할 기반 버프 우선순위 ===
            var targetSettings = Main.Settings?.GetOrCreateSettings(target.UniqueId, target.CharacterName);
            var targetRole = targetSettings != null ? GetRoleFromSettings(targetSettings) : UnitRole.Balanced;

            // 버프 타입 분석 (능력 이름 기반)
            BuffType buffType = AnalyzeBuffType(ability);

            switch (buffType)
            {
                case BuffType.Attack:
                    // 공격 버프는 DPS/Sniper에게 우선
                    if (targetRole == UnitRole.DPS || targetRole == UnitRole.Sniper)
                    {
                        score += 50f;
                        Main.LogDebug($"[BuffPriority] Attack buff -> {target.CharacterName} (DPS/Sniper): +50");
                    }
                    else if (targetRole == UnitRole.Hybrid)
                    {
                        score += 30f;
                    }
                    else if (targetRole == UnitRole.Tank)
                    {
                        score += 10f; // 탱커도 공격은 하지만 우선순위 낮음
                    }
                    break;

                case BuffType.Defense:
                    // 방어 버프는 Tank에게 우선, 또는 HP 낮은 아군
                    if (targetRole == UnitRole.Tank)
                    {
                        score += 50f;
                        Main.LogDebug($"[BuffPriority] Defense buff -> {target.CharacterName} (Tank): +50");
                    }
                    else if (GetHPPercent(target) < 50f)
                    {
                        score += 40f;
                        Main.LogDebug($"[BuffPriority] Defense buff -> {target.CharacterName} (Low HP): +40");
                    }
                    break;

                case BuffType.Accuracy:
                    // 정확도 버프는 Sniper/원거리에게 우선
                    if (targetRole == UnitRole.Sniper)
                    {
                        score += 50f;
                        Main.LogDebug($"[BuffPriority] Accuracy buff -> {target.CharacterName} (Sniper): +50");
                    }
                    else if (targetRole == UnitRole.DPS)
                    {
                        score += 35f;
                    }
                    break;

                case BuffType.Speed:
                    // 속도/AP 버프는 교전 중이거나 곧 행동할 유닛에게
                    if (target.CombatState?.IsEngaged ?? false)
                    {
                        score += 40f;
                    }
                    // 근접 딜러에게 속도 버프 유용
                    if (targetRole == UnitRole.Tank || targetRole == UnitRole.DPS)
                    {
                        score += 25f;
                    }
                    break;

                case BuffType.Generic:
                default:
                    // 일반 버프는 역할에 따라 균등하게
                    break;
            }

            // === 전투 상황 기반 우선순위 ===

            // 1. 교전 중인 아군 우선 (버프 효과 즉시 활용 가능)
            if (target.CombatState?.IsEngaged ?? false)
            {
                score += 25f;
            }

            // 2. 적과 가까운 아군 우선 (곧 전투할 가능성 높음)
            float distToNearestEnemy = GetDistanceToNearestEnemy(target, analysis);
            if (distToNearestEnemy < 10f)
            {
                score += 20f;
            }
            else if (distToNearestEnemy > 20f)
            {
                score -= 15f; // 너무 멀리 있으면 버프 효율 낮음
            }

            // 3. HP가 충분한 아군 우선 (HP 낮으면 힐이 먼저)
            float targetHP = GetHPPercent(target);
            if (targetHP > 70f)
            {
                score += 15f;
            }
            else if (targetHP < 40f)
            {
                score -= 20f; // HP 낮으면 버프보다 힐 필요
            }

            // 4. 가장 위협적인 적을 상대할 수 있는 아군
            if (analysis.HighestThreatEnemy != null)
            {
                float distToThreat = Vector3.Distance(target.Position, analysis.HighestThreatEnemy.Position);
                if (distToThreat < 15f)
                {
                    score += 20f;
                    Main.LogDebug($"[BuffPriority] {target.CharacterName} near highest threat: +20");
                }
            }

            // 5. 자가 버프 보너스 (서포터가 자기에게 쓰는 것도 유효)
            if (isSelf)
            {
                score += 10f;
                // 하지만 서포터가 아니면 다른 아군에게 주는 게 나음
                if (analysis.UnitRole != UnitRole.Support)
                {
                    score -= 5f;
                }
            }

            // 6. 이미 많은 버프가 있는 대상은 피함 (버프 분산)
            try
            {
                int existingBuffCount = CountExistingBuffs(target);
                if (existingBuffCount >= 3)
                {
                    score -= 20f;
                    Main.LogDebug($"[BuffPriority] {target.CharacterName} has {existingBuffCount} buffs already: -20");
                }
                else if (existingBuffCount == 0)
                {
                    score += 15f; // 버프 없는 아군 우선
                }
            }
            catch { }

            return score;
        }

        /// <summary>
        /// 버프 타입 분석
        /// </summary>
        enum BuffType { Generic, Attack, Defense, Accuracy, Speed }

        static BuffType AnalyzeBuffType(AbilityData ability)
        {
            if (ability?.Blueprint == null) return BuffType.Generic;

            string name = ability.Blueprint.name?.ToLowerInvariant() ?? "";
            string abilityName = ability.Name?.ToLowerInvariant() ?? "";
            string combined = name + " " + abilityName;

            // 공격 버프
            if (ContainsAny(combined, "damage", "attack", "strike", "power", "might", "strength",
                "fury", "rage", "empower", "reckless", "offensive"))
                return BuffType.Attack;

            // 방어 버프
            if (ContainsAny(combined, "defense", "protect", "shield", "armor", "guard", "ward",
                "resilience", "fortify", "iron", "unyielding", "defensive"))
                return BuffType.Defense;

            // 정확도 버프
            if (ContainsAny(combined, "accuracy", "aim", "precision", "focus", "keen", "sight",
                "ballistic", "marksm", "target"))
                return BuffType.Accuracy;

            // 속도/AP 버프
            if (ContainsAny(combined, "speed", "haste", "swift", "quick", "action", "move",
                "agility", "momentum", "rush"))
                return BuffType.Speed;

            return BuffType.Generic;
        }

        /// <summary>
        /// 대상에게 가장 가까운 적까지의 거리
        /// </summary>
        static float GetDistanceToNearestEnemy(BaseUnitEntity target, CombatAnalysis analysis)
        {
            float minDist = float.MaxValue;
            foreach (var enemy in analysis.Enemies)
            {
                if (enemy == null) continue;
                float dist = Vector3.Distance(target.Position, enemy.Position);
                if (dist < minDist) minDist = dist;
            }
            return minDist;
        }

        /// <summary>
        /// 원거리 공격의 실제 명중률 계산 (0-100)
        /// 게임의 RuleCalculateHitChances를 사용하여 정확한 값 계산
        /// </summary>
        static float EstimateHitChance(BaseUnitEntity attacker, BaseUnitEntity target, AbilityData ability)
        {
            try
            {
                // 능력이 없으면 주무기 기본 공격으로 계산
                AbilityData abilityToCheck = ability;
                if (abilityToCheck == null)
                {
                    // 주무기 공격 능력 찾기
                    var weapon = attacker.Body?.PrimaryHand?.MaybeWeapon;
                    if (weapon != null)
                    {
                        // 무기의 기본 공격 능력 사용
                        var weaponAbilities = attacker.Abilities?.RawFacts?
                            .Where(a => a.Data?.SourceItem == weapon)
                            .Select(a => a.Data)
                            .FirstOrDefault();

                        if (weaponAbilities != null)
                        {
                            abilityToCheck = weaponAbilities;
                        }
                    }
                }

                // 능력이 여전히 없으면 기본값 반환
                if (abilityToCheck == null)
                {
                    Main.LogDebug($"[HitChance] No ability to check for {attacker.CharacterName}");
                    return 70f;
                }

                // 게임의 실제 명중률 계산 룰 사용
                var hitChanceRule = new RuleCalculateHitChances(
                    attacker,
                    target,
                    abilityToCheck,
                    0,  // burstIndex
                    attacker.Position,
                    target.Position
                );

                // 룰 실행
                Rulebook.Trigger(hitChanceRule);

                float hitChance = hitChanceRule.ResultHitChance;
                float distanceFactor = hitChanceRule.DistanceFactor;
                var coverType = hitChanceRule.ResultLos;

                Main.LogDebug($"[HitChance] GAME CALC: {attacker.CharacterName} -> {target.CharacterName}: " +
                    $"{hitChance}% (DistFactor: {distanceFactor:F1}, Cover: {coverType}, " +
                    $"BS: {hitChanceRule.ResultBallisticSkill}, BaseBeforeRecoil: {hitChanceRule.ResultBaseChancesBeforeRecoil})");

                return hitChance;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[HitChance] Error using game calculation: {ex.Message}");

                // 폴백: 기존 추정 방식 사용
                return EstimateHitChanceFallback(attacker, target);
            }
        }

        /// <summary>
        /// 폴백 명중률 추정 (게임 룰 사용 실패 시)
        /// </summary>
        static float EstimateHitChanceFallback(BaseUnitEntity attacker, BaseUnitEntity target)
        {
            float baseHitChance = 70f;

            try
            {
                float distance = Vector3.Distance(attacker.Position, target.Position);

                // 무기 최적 사거리 확인
                float maxRange = 20f;
                try
                {
                    var weapon = attacker.Body?.PrimaryHand?.MaybeWeapon;
                    if (weapon != null)
                    {
                        maxRange = weapon.Blueprint?.AttackRange ?? 20f;
                    }
                }
                catch { }

                // 거리 계수 계산 (게임과 동일한 로직)
                float distanceFactor;
                if (distance <= maxRange / 2f)
                {
                    distanceFactor = 1.0f;  // 최적 거리
                }
                else if (distance <= maxRange)
                {
                    distanceFactor = 0.5f;  // 장거리
                }
                else
                {
                    distanceFactor = 0f;    // 사거리 밖
                }

                baseHitChance = baseHitChance * distanceFactor;

                // 엄폐 체크
                try
                {
                    var los = LosCalculations.GetWarhammerLos(attacker.Position, attacker.SizeRect, target);

                    switch (los.CoverType)
                    {
                        case LosCalculations.CoverType.Full:
                            Main.LogDebug($"[HitChance] Fallback: {target.CharacterName} in FULL cover");
                            break;
                        case LosCalculations.CoverType.Half:
                            Main.LogDebug($"[HitChance] Fallback: {target.CharacterName} in HALF cover");
                            break;
                        case LosCalculations.CoverType.Invisible:
                            baseHitChance = 0f;
                            Main.LogDebug($"[HitChance] Fallback: {target.CharacterName} INVISIBLE");
                            break;
                    }
                }
                catch { }

                baseHitChance = Mathf.Clamp(baseHitChance, 0f, 95f);
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[HitChance] Fallback error: {ex.Message}");
            }

            return baseHitChance;
        }

        /// <summary>
        /// 대상의 기존 버프 수 계산
        /// </summary>
        static int CountExistingBuffs(BaseUnitEntity target)
        {
            int count = 0;
            try
            {
                var facts = target.Facts?.List;
                if (facts != null)
                {
                    foreach (var fact in facts)
                    {
                        // 버프성 팩트 카운트 (정확한 판별은 게임 구조에 따라 다름)
                        string factName = fact?.Blueprint?.name?.ToLowerInvariant() ?? "";
                        if (ContainsAny(factName, "buff", "enhance", "empower", "boost", "bless",
                            "inspire", "command", "order", "strengthen"))
                        {
                            count++;
                        }
                    }
                }
            }
            catch { }
            return count;
        }

        /// <summary>
        /// 디버프 점수 계산
        /// </summary>
        static float ScoreDebuff(BaseUnitEntity target, CombatAnalysis analysis)
        {
            float score = 30f;

            // 강한 적에게 디버프 우선
            float targetHP = GetHPPercent(target);
            if (targetHP > 70f)
            {
                score += 30f;
            }

            // 집중 타겟에게 디버프 보너스
            if (target == analysis.FocusTarget)
            {
                score += 20f;
            }

            return score;
        }

        /// <summary>
        /// 방어 점수 계산
        /// </summary>
        static float ScoreDefense(BaseUnitEntity target, BaseUnitEntity unit,
            CombatAnalysis analysis, bool isSelf)
        {
            float score = 0f;
            float targetHP = GetHPPercent(target);

            // 교전 중이거나 HP가 낮으면 방어 우선
            if (analysis.IsEngaged || targetHP < 50f)
            {
                score += 50f;
            }

            // 가장 약한 아군 보호
            if (target == analysis.WeakestAlly && analysis.WeakestAllyHP < 50f)
            {
                score += 40f;
            }

            // 전투 시작에는 방어보다 버프/공격
            if (analysis.Phase == CombatPhase.Opening)
            {
                score -= 20f;
            }

            return score;
        }

        /// <summary>
        /// 도발 점수 계산 (탱커 전용)
        /// </summary>
        static float ScoreTaunt(BaseUnitEntity unit, CombatAnalysis analysis)
        {
            float score = 50f; // 기본 점수

            // 탱커 역할이면 도발 보너스
            if (analysis.UnitRole == UnitRole.Tank)
            {
                score += 80f;
            }

            // 주변에 적이 많을수록 보너스
            int nearbyEnemies = analysis.NearbyEnemies;
            score += nearbyEnemies * 15f;

            // 위급한 아군이 있으면 도발로 어그로 끌기 우선
            if (analysis.CriticalAlliesCount > 0)
            {
                score += 40f;
            }

            // 전투 초반에 도발 우선
            if (analysis.Phase == CombatPhase.Opening)
            {
                score += 30f;
            }

            return score;
        }

        /// <summary>
        /// Exploit 점수 계산 (약점 공략)
        /// </summary>
        static float ScoreExploit(BaseUnitEntity target, CombatAnalysis analysis)
        {
            float score = 40f; // 기본 점수

            // 강한 적에게 Exploit 우선
            float targetHP = GetHPPercent(target);
            if (targetHP > 60f)
            {
                score += 30f;
            }

            // 집중 타겟에게 보너스
            if (target == analysis.FocusTarget)
            {
                score += 25f;
            }

            return score;
        }

        /// <summary>
        /// 이동 점수 계산 (역할별 방향 결정)
        /// </summary>
        static float ScoreMovement(BaseUnitEntity unit, BaseUnitEntity target,
            Settings.CharacterSettings settings, CombatAnalysis analysis)
        {
            float score = -20f; // 기본적으로 이동보다 행동 우선

            // 적이 없으면 이동 불필요
            if (analysis.Enemies.Count == 0)
            {
                return -100f;
            }

            // 가장 가까운 적까지의 거리 계산
            float distanceToNearestEnemy = float.MaxValue;
            BaseUnitEntity nearestEnemy = null;

            foreach (var enemy in analysis.Enemies)
            {
                float dist = Vector3.Distance(unit.Position, enemy.Position);
                if (dist < distanceToNearestEnemy)
                {
                    distanceToNearestEnemy = dist;
                    nearestEnemy = enemy;
                }
            }

            // === 역할별 이동 로직 ===
            switch (analysis.UnitRole)
            {
                case UnitRole.Tank:
                    // 탱커: 적에게 접근 (전방 유지)
                    if (distanceToNearestEnemy > 10f) // 적이 멀리 있으면 접근
                    {
                        score += 60f;
                        Main.LogDebug($"[Movement] Tank {unit.CharacterName} - 적이 멀리 있음, 접근 필요");
                    }
                    else if (distanceToNearestEnemy < 3f)
                    {
                        // 이미 가까우면 이동 불필요
                        score -= 50f;
                    }
                    // 탱커가 후방에 있으면 전진 필요
                    if (!analysis.IsEngaged && analysis.NearbyEnemies == 0)
                    {
                        score += 40f;
                        Main.LogDebug($"[Movement] Tank {unit.CharacterName} - 교전 중 아님, 전진 필요");
                    }
                    break;

                case UnitRole.DPS:
                    // DPS: 근접 선호면 접근, 원거리면 사거리 유지
                    if (settings.RangePreference == Settings.RangePreference.PreferMelee)
                    {
                        if (distanceToNearestEnemy > 5f)
                        {
                            score += 30f;
                        }
                    }
                    else if (settings.RangePreference == Settings.RangePreference.PreferRanged)
                    {
                        if (distanceToNearestEnemy < 8f)
                        {
                            score += 20f; // 약간의 거리 유지 보너스
                        }
                    }
                    break;

                case UnitRole.Support:
                    // 서포터: 아군 근처 유지, 적에게서 멀리
                    if (distanceToNearestEnemy < 5f)
                    {
                        score += 40f; // 적에게 너무 가까우면 후퇴 고려
                    }
                    // 카이팅 필요시 최우선으로 이동
                    if (analysis.NeedsKiting)
                    {
                        score += 80f;
                        Main.LogDebug($"[Kiting] Support {unit.CharacterName}: URGENT retreat from {analysis.KitingThreat?.CharacterName}");
                    }
                    break;

                case UnitRole.Sniper:
                    // 스나이퍼: 항상 거리 유지
                    if (distanceToNearestEnemy < 15f)
                    {
                        score += 50f; // 거리 유지 필요
                    }
                    // 카이팅 필요시 최우선으로 이동
                    if (analysis.NeedsKiting)
                    {
                        score += 100f; // 스나이퍼는 카이팅 최우선
                        Main.LogDebug($"[Kiting] Sniper {unit.CharacterName}: CRITICAL retreat from {analysis.KitingThreat?.CharacterName}");
                    }
                    break;

                default:
                    // Balanced/Hybrid: 상황에 따라
                    if (settings.RangePreference == Settings.RangePreference.Adaptive)
                    {
                        // 행동할 수 있으면 이동 안 함
                        score -= 20f;
                    }
                    break;
            }

            // === 원거리 공격자의 명중률 기반 이동 판단 ===
            // 원거리 선호인데 명중률이 낮으면 더 좋은 위치로 이동
            if (settings.RangePreference == Settings.RangePreference.PreferRanged ||
                settings.RangePreference == Settings.RangePreference.MaintainRange ||
                analysis.UnitRole == UnitRole.Sniper)
            {
                // 가장 가까운 적에 대한 명중률 체크
                if (nearestEnemy != null)
                {
                    // 현재 장착 무기로 예상 명중률 계산
                    float estimatedHitChance = EstimateHitChance(unit, nearestEnemy, null);

                    if (estimatedHitChance < MIN_ACCEPTABLE_HIT_CHANCE)
                    {
                        // 명중률이 60% 미만이면 이동 점수 상승 (0%에서 최대 보너스)
                        float moveBonus = (MIN_ACCEPTABLE_HIT_CHANCE - estimatedHitChance) * 2f;
                        score += moveBonus;
                        Main.Log($"[Movement] LOW HIT CHANCE - SHOULD REPOSITION: " +
                            $"{unit.CharacterName} -> {nearestEnemy.CharacterName}, " +
                            $"Current hit chance: {estimatedHitChance:F0}%, Move bonus: +{moveBonus:F0}");
                    }
                }
            }

            // 교전 중인데 이동하면 기회 공격 받을 수 있음
            if (analysis.IsEngaged)
            {
                score -= 30f;
            }

            return score;
        }

        /// <summary>
        /// 역할별 보너스 계산 (프로필 기반 적응형)
        /// </summary>
        static float GetRoleBonus(AbilityCategory category, UnitRole role, CombatAnalysis analysis)
        {
            float bonus = 0f;
            var profile = analysis.Profile;

            switch (role)
            {
                case UnitRole.Balanced:
                    // 상황에 따라 유동적
                    if (analysis.TeamNeedsHealing && category == AbilityCategory.Heal)
                        bonus += 30f;
                    if (analysis.Phase == CombatPhase.Cleanup && category == AbilityCategory.Attack)
                        bonus += 20f;
                    if (analysis.Phase == CombatPhase.Opening && category == AbilityCategory.Buff)
                        bonus += 20f;
                    break;

                case UnitRole.DPS:
                    if (category == AbilityCategory.Attack) bonus += 40f;
                    if (category == AbilityCategory.Debuff) bonus += 20f;
                    // 약점 공략 (Operative Analyse Enemies 등)
                    if (category == AbilityCategory.Exploit) bonus += SCORE_EXPLOIT_BONUS;
                    // DPS도 마무리 단계에서 공격 추가 보너스
                    if (analysis.Phase == CombatPhase.Cleanup && category == AbilityCategory.Attack)
                        bonus += 20f;
                    break;

                case UnitRole.Tank:
                    // 도발 스킬이 있으면 최우선!
                    if (category == AbilityCategory.Taunt)
                    {
                        bonus += SCORE_TAUNT_BONUS;
                    }
                    // 탱커는 공격을 우선! (버프보다 높게)
                    if (category == AbilityCategory.Attack)
                    {
                        bonus += 80f; // 기본 공격 보너스
                        if (analysis.IsEngaged) bonus += 30f; // 교전 중 추가 보너스
                    }
                    // 방어 스킬
                    if (category == AbilityCategory.Defense)
                    {
                        if (profile != null && profile.TauntSkills == 0)
                            bonus += 50f; // 도발 없으면 방어 강화
                        else
                            bonus += 30f;
                    }
                    // 탱커는 버프보다 공격/이동 우선 - 버프 페널티
                    // v2.0.9: 너무 높은 페널티는 중요한 방어 버프 사용을 막음
                    if (category == AbilityCategory.Buff)
                    {
                        // 교전 중이 아니면 버프 페널티 (이동 우선해야 함)
                        if (!analysis.IsEngaged)
                            bonus -= 40f; // 교전 전 버프 약간 억제 (중요 버프는 사용 가능)
                        else
                            bonus -= 15f; // 교전 중에는 페널티 적게
                    }
                    // 탱커가 교전 중이 아니면 이동(돌격) 우선
                    if (category == AbilityCategory.Movement)
                    {
                        if (!analysis.IsEngaged)
                        {
                            bonus += 60f; // 교전 전 이동 우선
                        }
                    }
                    // 탱커도 여유있을 때 힐 가능
                    if (category == AbilityCategory.Heal && analysis.TeamNeedsHealing) bonus += 30f;
                    break;

                case UnitRole.Support:
                    // 서포터 스킬이 실제로 있으면 보너스
                    if (category == AbilityCategory.Buff)
                    {
                        bonus += 40f;
                    }
                    if (category == AbilityCategory.Debuff)
                    {
                        bonus += 35f;
                    }
                    // 힐 스킬이 있으면 힐 보너스
                    if (category == AbilityCategory.Heal)
                    {
                        if (analysis.TeamNeedsHealing) bonus += 50f;
                        else bonus += 20f;
                    }
                    // 서포터인데 공격 스킬밖에 없으면 공격해야 함
                    if (profile != null && profile.BuffSkills == 0 && profile.DebuffSkills == 0 && profile.HealSkills == 0)
                    {
                        if (category == AbilityCategory.Attack) bonus += 30f;
                    }
                    break;

                case UnitRole.Hybrid:
                    // 하이브리드는 근접/원거리 모두 유연하게
                    if (category == AbilityCategory.Attack) bonus += 30f;
                    // 교전 중이면 근접 공격 보너스
                    if (analysis.IsEngaged && category == AbilityCategory.Attack) bonus += 15f;
                    // 여유 있으면 버프/디버프도
                    if (category == AbilityCategory.Buff) bonus += 15f;
                    if (category == AbilityCategory.Debuff) bonus += 15f;
                    // 팀 힐 필요시 힐도 가능
                    if (analysis.TeamNeedsHealing && category == AbilityCategory.Heal) bonus += 25f;
                    break;

                case UnitRole.Sniper:
                    // 스나이퍼는 공격 우선, 원거리 선호
                    if (category == AbilityCategory.Attack) bonus += 45f;
                    if (category == AbilityCategory.Debuff) bonus += 15f;
                    // 방어/이동 페널티 (자리 유지 선호)
                    if (category == AbilityCategory.Defense) bonus -= 10f;
                    break;
            }

            return bonus;
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// 가능한 타겟 목록 가져오기
        /// </summary>
        static List<TargetWrapper> GetPossibleTargets(DecisionContext context, AbilityData ability, CombatAnalysis analysis)
        {
            var targets = new List<TargetWrapper>();

            try
            {
                var blueprint = ability.Blueprint;
                if (blueprint == null) return targets;

                // 어빌리티 카테고리 확인
                var category = CategorizeAbility(ability);

                // === Zone 스킬 (Keystone) 특수 처리 ===
                // 커스텀 Zone 핸들러: 겹침 체크 + 아군 위치 기반 최적화
                string bpNameLower = blueprint.name?.ToLower() ?? "";
                if (bpNameLower.Contains("keystone"))
                {
                    try
                    {
                        var bestTarget = FindBestZonePosition(context, ability, analysis);
                        if (bestTarget != null)
                        {
                            targets.Add(bestTarget);
                            Main.LogDebug($"[CustomAI] Zone skill {ability.Name}: custom position found at {bestTarget.Point}");
                        }
                        else
                        {
                            Main.LogDebug($"[CustomAI] Zone skill {ability.Name}: no valid non-overlapping position");
                        }
                    }
                    catch (Exception ex)
                    {
                        Main.LogError($"[CustomAI] Zone skill {ability.Name} error: {ex.Message}");
                    }
                    return targets;
                }

                // DangerousAoE 체크 - 이 능력은 자기 자신이나 아군을 타겟으로 추가하면 안 됨
                bool isDangerousAoE = IsDangerousAoE(ability);
                if (isDangerousAoE)
                {
                    Main.LogDebug($"[CustomAI] DangerousAoE ability {ability.Name}: skipping self/ally targeting");
                }

                // 공격/디버프 스킬인지 확인 - 이런 스킬은 아군을 타겟으로 절대 추가하면 안 됨
                // v2.0.9: Taunt는 자기 중심 AoE이므로 isOffensiveAbility에서 제외!
                // Taunt는 자기 자신을 타겟으로 하고, 패턴으로 주변 적에게 영향을 줌
                bool isOffensiveAbility = category == AbilityCategory.Attack ||
                                          category == AbilityCategory.Debuff;
                bool isTaunt = category == AbilityCategory.Taunt;

                if (isOffensiveAbility)
                {
                    Main.LogDebug($"[CustomAI] Offensive ability {ability.Name} ({category}): will only target enemies");
                }

                // 자기 자신 (DangerousAoE/Offensive가 아닐 때만, 단 Taunt는 예외)
                // Taunt는 자기 중심 AoE이므로 자신을 타겟으로 해야 함
                if (isTaunt)
                {
                    targets.Add(new TargetWrapper(context.Unit));
                    Main.LogDebug($"[CustomAI] Taunt ability {ability.Name}: targeting self (AoE centered on caster)");
                }
                else if (blueprint.CanTargetSelf && !isDangerousAoE && !isOffensiveAbility)
                {
                    targets.Add(new TargetWrapper(context.Unit));
                }

                // 적
                if (blueprint.CanTargetEnemies)
                {
                    foreach (var enemy in analysis.Enemies)
                    {
                        targets.Add(new TargetWrapper(enemy));
                    }
                }

                // 아군 (DangerousAoE/Offensive/Taunt가 아닐 때만 - 공격/디버프/도발 스킬은 절대 아군 타겟 불가!)
                if (blueprint.CanTargetFriends && !isDangerousAoE && !isOffensiveAbility && !isTaunt)
                {
                    foreach (var ally in analysis.Allies)
                    {
                        targets.Add(new TargetWrapper(ally));
                    }
                }

                // === 특수 케이스: AoE/Self-cast 어빌리티 ===
                // Taunt, Defense 등 자기 중심 AoE는 자신을 타겟으로 추가
                // DangerousAoE/Offensive 스킬은 절대 자기 자신을 타겟으로 추가하지 않음!
                if (targets.Count == 0 && !isDangerousAoE && !isOffensiveAbility)
                {
                    // Personal 범위 어빌리티
                    if (blueprint.Range == AbilityRange.Personal)
                    {
                        targets.Add(new TargetWrapper(context.Unit));
                        Main.LogDebug($"[CustomAI] Added self as target for Personal ability: {ability.Name}");
                    }
                    // Taunt 카테고리인데 타겟이 없으면 자신 추가 (AoE 도발)
                    else if (category == AbilityCategory.Taunt)
                    {
                        targets.Add(new TargetWrapper(context.Unit));
                        Main.LogDebug($"[CustomAI] Added self as target for Taunt AoE: {ability.Name}");
                    }
                    // Defense 카테고리도 자기 버프일 수 있음
                    else if (category == AbilityCategory.Defense)
                    {
                        targets.Add(new TargetWrapper(context.Unit));
                        Main.LogDebug($"[CustomAI] Added self as target for Defense ability: {ability.Name}");
                    }
                    // Buff 카테고리도 자기 버프일 수 있음
                    else if (category == AbilityCategory.Buff)
                    {
                        targets.Add(new TargetWrapper(context.Unit));
                        Main.LogDebug($"[CustomAI] Added self as target for Buff ability: {ability.Name}");
                    }
                    // 블루프린트 이름으로 AoE 판단 (scream, shout, aura 등) - 단, Offensive 제외
                    else if (!isOffensiveAbility)
                    {
                        string bpName = blueprint.name?.ToLower() ?? "";
                        if (ContainsAny(bpName, "scream", "shout", "aura", "aoe", "area", "burst", "nova", "pulse"))
                        {
                            targets.Add(new TargetWrapper(context.Unit));
                            Main.LogDebug($"[CustomAI] Added self as target for AoE keyword ability: {ability.Name}");
                        }
                    }
                }
                else if (targets.Count == 0 && (isDangerousAoE || isOffensiveAbility))
                {
                    Main.Log($"[CustomAI] {(isDangerousAoE ? "DangerousAoE" : "Offensive")} {ability.Name}: No valid targets found");
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[CustomAI] GetPossibleTargets error: {ex}");
            }

            return targets;
        }

        #endregion

        #region Helper Methods

        static float GetHPPercent(BaseUnitEntity unit)
        {
            if (unit?.Health == null || unit.Health.MaxHitPoints == 0)
                return 100f;
            return (float)unit.Health.HitPointsLeft / unit.Health.MaxHitPoints * 100f;
        }

        static string GetTargetName(TargetWrapper target)
        {
            var entity = target?.Entity as BaseUnitEntity;
            return entity?.CharacterName ?? target?.ToString() ?? "Unknown";
        }

        #endregion

        #region Data Classes

        class CombatAnalysis
        {
            // 유닛 목록
            public List<BaseUnitEntity> Enemies = new List<BaseUnitEntity>();
            public List<BaseUnitEntity> Allies = new List<BaseUnitEntity>();

            // 자신의 상태
            public float UnitHPPercent;
            public bool IsEngaged;
            public UnitRole UnitRole;
            public GameArchetype Archetype;  // 감지된 아키타입

            // 어빌리티 프로필 (실제로 가진 스킬 분석)
            public AbilityProfile Profile;

            // 적 분석
            public BaseUnitEntity NearestEnemy;
            public float NearestEnemyDistance = float.MaxValue;
            public BaseUnitEntity WeakestEnemy;
            public float WeakestEnemyHP = 100f;
            public BaseUnitEntity FocusTarget; // 팀 집중 타겟
            public int NearbyEnemies; // 근처 적 수 (도발 범위 내)

            // 위협도 분석
            public BaseUnitEntity HighestThreatEnemy; // 가장 위험한 적 (데미지 딜러)
            public float HighestThreatScore = 0f;
            public Dictionary<BaseUnitEntity, float> EnemyThreatScores = new Dictionary<BaseUnitEntity, float>();

            // 카이팅 필요 여부
            public bool NeedsKiting; // 원거리 캐릭터가 근접 위협에 노출됨
            public BaseUnitEntity KitingThreat; // 카이팅 해야 할 대상

            // 아군 분석
            public BaseUnitEntity WeakestAlly;
            public float WeakestAllyHP = 100f;
            public int CriticalAlliesCount; // HP < 30%
            public int WoundedAlliesCount;  // HP < 70%
            public bool TeamNeedsHealing;

            // 전투 단계
            public CombatPhase Phase;
        }

        /// <summary>
        /// 캐릭터가 실제로 보유한 어빌리티 프로필
        /// </summary>
        class AbilityProfile
        {
            public int MeleeAttacks;
            public int RangedAttacks;
            public int DefenseSkills;
            public int TauntSkills;
            public int BuffSkills;
            public int DebuffSkills;
            public int HealSkills;
            public int TotalAttacks => MeleeAttacks + RangedAttacks;

            /// <summary>
            /// 근접 중심인지 (근접 공격 > 원거리 공격)
            /// </summary>
            public bool IsMeleeFocused => MeleeAttacks > RangedAttacks;

            /// <summary>
            /// 원거리 중심인지 (원거리 공격 > 근접 공격)
            /// </summary>
            public bool IsRangedFocused => RangedAttacks > MeleeAttacks;

            /// <summary>
            /// 탱커 스타일인지 (도발/방어 스킬 보유)
            /// </summary>
            public bool HasTankCapability => TauntSkills > 0 || DefenseSkills >= 2;

            /// <summary>
            /// 서포터 스타일인지 (버프/디버프/힐 다수)
            /// </summary>
            public bool HasSupportCapability => (BuffSkills + DebuffSkills + HealSkills) >= 3;

            public override string ToString()
            {
                return $"Melee:{MeleeAttacks} Ranged:{RangedAttacks} Def:{DefenseSkills} Taunt:{TauntSkills} Buff:{BuffSkills} Debuff:{DebuffSkills} Heal:{HealSkills}";
            }
        }

        class CategorizedAbility
        {
            public AbilityData Ability;
            public AbilityCategory Category;
            public int APCost;
            public bool IsMelee;
            public bool IsRanged;
        }

        #endregion

        #region Positioning Patch

        /// <summary>
        /// 역할 기반 위치 선정 패치 - 실제로 최적 위치를 찾아 이동
        /// - 탱커: 최전방 유지, HP 위급 시에만 엄폐
        /// - 스나이퍼: 최대한 거리 유지 + 엄폐 (카이팅)
        /// - 서포터: 중거리 유지 + 엄폐
        /// - DPS/하이브리드/밸런스: 공격 범위 내 최적 엄폐
        /// </summary>
        [HarmonyPatch(typeof(TaskNodeFindBetterPlace))]
        public static class FindBetterPlacePatch
        {
            // 역할별 가중치 설정
            private const float COVER_WEIGHT_SNIPER = 2.0f;
            private const float COVER_WEIGHT_SUPPORT = 1.5f;
            private const float COVER_WEIGHT_DPS = 1.0f;
            private const float COVER_WEIGHT_TANK = 0.3f;

            private const float DISTANCE_WEIGHT_SNIPER = 2.0f;   // 거리 유지 중요
            private const float DISTANCE_WEIGHT_SUPPORT = 1.0f;
            private const float DISTANCE_WEIGHT_DPS = 0.5f;
            private const float DISTANCE_WEIGHT_TANK = -1.5f;    // 적에게 가까이 (음수)

            private const float THREAT_WEIGHT_SNIPER = 2.0f;     // 위협 회피 중요
            private const float THREAT_WEIGHT_SUPPORT = 1.5f;
            private const float THREAT_WEIGHT_DPS = 1.0f;
            private const float THREAT_WEIGHT_TANK = 0.2f;       // 위협 무시

            [HarmonyPatch("TickInternal")]
            [HarmonyPrefix]
            static bool Prefix(TaskNodeFindBetterPlace __instance, Blackboard blackboard, ref Status __result)
            {
                try
                {
                    var context = blackboard?.DecisionContext;
                    if (context == null)
                    {
                        Main.LogDebug("[Positioning] PATCH RUNNING but context is null");
                        return true;
                    }

                    var unit = context.Unit;
                    if (unit == null)
                    {
                        Main.LogDebug("[Positioning] PATCH RUNNING but unit is null");
                        return true;
                    }

                    Main.Log($"[Positioning] === FindBetterPlace Patch for {unit.CharacterName} ===");

                    if (!unit.IsDirectlyControllable)
                    {
                        Main.LogDebug($"[Positioning] {unit.CharacterName} is not directly controllable, skipping");
                        return true;
                    }

                    var settings = Main.Settings.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
                    if (!settings.EnableCustomAI)
                    {
                        Main.LogDebug($"[Positioning] {unit.CharacterName} CustomAI disabled, skipping");
                        return true;
                    }

                    var role = GetRoleFromSettings(settings);
                    float hpPercent = GetHPPercent(unit);
                    bool isEngaged = unit.CombatState?.IsEngaged ?? false;

                    Main.Log($"[Positioning] {unit.CharacterName} Role={role}, HP={hpPercent:F0}%, Engaged={isEngaged}");

                    // ========== Support 역할: 현재 위치 유지 우선 ==========
                    // Support는 위험하지 않으면 굳이 이동하지 않음
                    if (role == UnitRole.Support)
                    {
                        // 교전 중이 아니고 HP가 괜찮으면 현재 위치 유지
                        if (!isEngaged && hpPercent >= 50f)
                        {
                            Main.Log($"[Positioning] Support {unit.CharacterName}: STAY IN PLACE (not engaged, HP OK)");
                            __result = Status.Failure; // 이동하지 않음
                            return false;
                        }
                        Main.Log($"[Positioning] Support {unit.CharacterName}: May need to move (engaged={isEngaged}, HP={hpPercent:F0}%)");
                    }

                    // 탱커: HP 충분하고 교전 중이면 현재 위치 유지
                    // v2.0.11: 단, 근접 범위에 적이 있어야 함 (근접 공격 가능해야 유지)
                    if (role == UnitRole.Tank && hpPercent >= 40f && isEngaged)
                    {
                        // 근접 범위에 적이 있는지 확인
                        bool anyEnemyInMeleeRange = false;
                        var enemiesCheck = context.Enemies;
                        if (enemiesCheck != null)
                        {
                            foreach (var targetInfo in enemiesCheck)
                            {
                                var enemyUnit = targetInfo?.Entity as BaseUnitEntity;
                                if (enemyUnit != null && Vector3.Distance(unit.Position, enemyUnit.Position) <= 3.5f)
                                {
                                    anyEnemyInMeleeRange = true;
                                    break;
                                }
                            }
                        }

                        if (anyEnemyInMeleeRange)
                        {
                            Main.Log($"[Positioning] Tank {unit.CharacterName}: Holding frontline (HP: {hpPercent:F0}%)");
                            __result = Status.Failure;
                            return false;
                        }
                        else
                        {
                            Main.Log($"[Positioning] Tank {unit.CharacterName}: No enemy in melee, need to advance");
                            // 이동 계속 진행 (적에게 접근)
                        }
                    }

                    // 이동 가능한 노드가 없으면 현재 위치 유지 (기본 AI 아님!)
                    var moveVariants = context.UnitMoveVariants;
                    if (moveVariants.IsZero || moveVariants.cells == null || moveVariants.cells.Count == 0)
                    {
                        Main.Log($"[Positioning] {unit.CharacterName}: No move variants, staying in place");
                        __result = Status.Failure;
                        return false;
                    }

                    // 적 목록
                    var enemies = context.Enemies;
                    if (enemies == null || enemies.Count == 0)
                    {
                        Main.Log($"[Positioning] {unit.CharacterName}: No enemies, staying in place");
                        __result = Status.Failure;
                        return false;
                    }

                    // ========== Support 역할 추가 보호: 적에게 더 가까워지면 이동 안함 ==========
                    if (role == UnitRole.Support || role == UnitRole.Sniper)
                    {
                        float currentMinDistance = GetMinDistanceToEnemies(unit, enemies);
                        Main.Log($"[Positioning] {unit.CharacterName}: Current min distance to enemies = {currentMinDistance:F1}");

                        // 이미 충분히 멀리 있으면 이동 안함
                        if (currentMinDistance >= 6f)
                        {
                            Main.Log($"[Positioning] Support/Sniper {unit.CharacterName}: Already at safe distance ({currentMinDistance:F1}), staying");
                            __result = Status.Failure;
                            return false;
                        }
                    }

                    // 최적 위치 찾기
                    var bestPosition = FindBestPosition(context, unit, role, hpPercent, isEngaged, enemies, settings);

                    if (bestPosition.HasValue)
                    {
                        var (bestNode, bestCell) = bestPosition.Value;
                        var currentNode = unit.CurrentUnwalkableNode;

                        // Support/Sniper: 이동 목적지가 적에게 더 가까우면 이동 안함
                        if (role == UnitRole.Support || role == UnitRole.Sniper)
                        {
                            float currentMinDist = GetMinDistanceToEnemies(unit, enemies);
                            float newMinDist = GetMinDistanceToEnemiesFromNode(bestNode, unit.SizeRect, enemies);

                            if (newMinDist < currentMinDist)
                            {
                                Main.Log($"[Positioning] BLOCKED! {unit.CharacterName}: Would move CLOSER to enemies ({currentMinDist:F1} -> {newMinDist:F1})");
                                __result = Status.Failure;
                                return false;
                            }
                        }

                        // 현재 위치보다 좋은 위치가 있으면 이동
                        if (bestNode != currentNode)
                        {
                            context.FoundBetterPlace = new DecisionContext.BetterPlace
                            {
                                PathData = moveVariants,
                                BestCell = bestCell
                            };
                            Main.Log($"[Positioning] {unit.CharacterName} ({role}): Moving to better position");
                            __result = Status.Success;
                            return false;
                        }
                        else
                        {
                            // 현재 위치가 최적
                            context.FoundBetterPlace = new DecisionContext.BetterPlace
                            {
                                PathData = moveVariants,
                                BestCell = moveVariants.startCell
                            };
                            Main.Log($"[Positioning] {unit.CharacterName} ({role}): Already at optimal position");
                            __result = Status.Success;
                            return false;
                        }
                    }

                    // 좋은 위치를 못 찾으면 현재 위치 유지 (기본 AI 호출 안함!)
                    Main.Log($"[Positioning] {unit.CharacterName}: No better position found, staying in place");
                    __result = Status.Failure;
                    return false;
                }
                catch (Exception ex)
                {
                    Main.LogError($"[Positioning] Error: {ex}");
                    return true;
                }
            }

            /// <summary>
            /// 역할별 최적 위치 찾기
            /// </summary>
            static (CustomGridNodeBase node, WarhammerPathAiCell cell)? FindBestPosition(
                DecisionContext context,
                BaseUnitEntity unit,
                UnitRole role,
                float hpPercent,
                bool isEngaged,
                List<TargetInfo> enemies,
                Settings.CharacterSettings settings)
            {
                var moveVariants = context.UnitMoveVariants;
                var unitSizeRect = unit.SizeRect;
                float availableAP = unit.CombatState?.ActionPointsBlue ?? 0f;

                // 가중치 설정
                float coverWeight, distanceWeight, threatWeight;
                float idealDistance;

                switch (role)
                {
                    case UnitRole.Sniper:
                        coverWeight = COVER_WEIGHT_SNIPER;
                        distanceWeight = DISTANCE_WEIGHT_SNIPER;
                        threatWeight = THREAT_WEIGHT_SNIPER;
                        idealDistance = 12f; // 최대 거리 유지
                        break;
                    case UnitRole.Support:
                        coverWeight = COVER_WEIGHT_SUPPORT;
                        distanceWeight = DISTANCE_WEIGHT_SUPPORT;
                        threatWeight = THREAT_WEIGHT_SUPPORT;
                        idealDistance = 8f; // 중거리
                        break;
                    case UnitRole.Tank:
                        coverWeight = COVER_WEIGHT_TANK;
                        distanceWeight = DISTANCE_WEIGHT_TANK;
                        threatWeight = THREAT_WEIGHT_TANK;
                        idealDistance = 1f; // 근접
                        break;
                    case UnitRole.DPS:
                    case UnitRole.Hybrid:
                    case UnitRole.Balanced:
                    default:
                        coverWeight = COVER_WEIGHT_DPS;
                        distanceWeight = DISTANCE_WEIGHT_DPS;
                        threatWeight = THREAT_WEIGHT_DPS;
                        // v2.0.8: 근접 선호 설정에 따라 이상 거리 조정
                        if (settings.RangePreference == Settings.RangePreference.PreferMelee)
                        {
                            idealDistance = 1f; // 근접 - 탱커처럼 붙어서 싸움
                            Main.LogDebug($"[FindBestPosition] {unit.CharacterName}: PreferMelee - idealDistance=1");
                        }
                        else
                        {
                            idealDistance = 6f; // 중거리
                        }
                        break;
                }

                // HP 낮으면 엄폐 가중치 증가
                if (hpPercent < 50f)
                {
                    coverWeight *= 1.5f;
                    threatWeight *= 1.5f;
                }

                // 교전 중인 원거리 캐릭터는 거리 유지 최우선 (카이팅)
                if (isEngaged && (role == UnitRole.Sniper || role == UnitRole.Support))
                {
                    distanceWeight *= 2.5f; // 거리 유지 강화
                    threatWeight *= 2.0f;   // 위협 회피 강화
                    idealDistance += 5f;    // 더 멀리 떨어지려 함
                    Main.LogDebug($"[Kiting] {unit.CharacterName}: Engaged ranged unit - increasing distance weight");
                }

                CustomGridNodeBase bestNode = null;
                WarhammerPathAiCell bestCell = default;
                float bestScore = float.MinValue;

                // v2.0.8: 디버그 - 평가할 셀 개수와 AP 로그
                int totalCells = moveVariants.cells.Count;
                int validCells = 0;
                float closestEnemyDist = float.MaxValue;

                foreach (var kvp in moveVariants.cells)
                {
                    var node = kvp.Key as CustomGridNodeBase;
                    var cell = kvp.Value;

                    if (node == null || !cell.IsCanStand)
                        continue;

                    // AP 체크 - 이동 후 행동할 AP가 있어야 함
                    if (cell.Length > availableAP)
                        continue;

                    validCells++;

                    // v2.0.8: PreferMelee 체크
                    bool preferMelee = settings.RangePreference == Settings.RangePreference.PreferMelee;

                    float score = CalculateNodeScore(
                        node, unitSizeRect, enemies,
                        coverWeight, distanceWeight, threatWeight,
                        idealDistance, role, preferMelee);

                    // v2.0.7: 이동 비용 패널티 - 실제 경로가 길면 감점
                    // 이렇게 하면 장애물을 돌아가야 하는 셀보다 직접 갈 수 있는 셀을 선호
                    float movementPenalty = cell.Length * 0.5f;

                    // v2.0.8: 탱커 또는 근접 선호는 이동 페널티 완전 무시
                    // 적에게 가까이 가는 것이 최우선!
                    if (role == UnitRole.Tank || preferMelee)
                    {
                        // 이동 페널티 적용 안함 - 가까이 가는게 중요
                        // 오히려 이동하면 보너스 (현재 위치에 머무르지 않도록)
                        if (cell.Length > 0)
                        {
                            score += 5f; // 이동 보너스
                        }
                    }
                    else
                    {
                        score -= movementPenalty;
                    }

                    // 이 셀에서 가장 가까운 적까지의 거리 체크
                    foreach (var enemy in enemies)
                    {
                        if (enemy?.Entity == null) continue;
                        float dist = Vector3.Distance(node.Vector3Position, enemy.Entity.Position);
                        if (dist < closestEnemyDist) closestEnemyDist = dist;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestNode = node;
                        bestCell = cell;
                    }
                }

                // v2.0.8: 디버그 요약
                Main.LogDebug($"[FindBestPosition] {unit.CharacterName}: Total cells={totalCells}, Valid cells={validCells}, " +
                    $"AvailableAP={availableAP:F1}, ClosestEnemyInReachableCells={closestEnemyDist:F1}");

                if (bestNode != null)
                {
                    // v2.0.8: 선택된 위치와 가장 가까운 적과의 거리 로그
                    float minDistToEnemy = float.MaxValue;
                    string nearestEnemyName = "?";
                    foreach (var enemy in enemies)
                    {
                        if (enemy?.Entity == null) continue;
                        float dist = Vector3.Distance(bestNode.Vector3Position, enemy.Entity.Position);
                        if (dist < minDistToEnemy)
                        {
                            minDistToEnemy = dist;
                            var enemyUnit = enemy.Entity as BaseUnitEntity;
                            nearestEnemyName = enemyUnit?.CharacterName ?? "?";
                        }
                    }
                    Main.LogDebug($"[FindBestPosition] {unit.CharacterName}: Selected position score={bestScore:F1}, " +
                        $"nearest enemy={nearestEnemyName} at distance={minDistToEnemy:F1}, idealDistance={idealDistance}");

                    return (bestNode, bestCell);
                }

                return null;
            }

            /// <summary>
            /// 노드 점수 계산
            /// </summary>
            static float CalculateNodeScore(
                CustomGridNodeBase node,
                IntRect unitSizeRect,
                List<TargetInfo> enemies,
                float coverWeight,
                float distanceWeight,
                float threatWeight,
                float idealDistance,
                UnitRole role,
                bool preferMelee = false)
            {
                float coverScore = 0f;
                float distanceScore = 0f;
                float threatScore = 0f;
                int enemyCount = enemies.Count;

                if (enemyCount == 0) return 0f;

                foreach (var enemy in enemies)
                {
                    if (enemy?.Entity == null) continue;

                    var enemyNode = enemy.Node;
                    var enemyRect = enemy.Entity.SizeRect;

                    // 1. 엄폐 점수 및 시야선 체크
                    try
                    {
                        var los = LosCalculations.GetWarhammerLos(enemyNode, enemyRect, node, unitSizeRect);
                        switch (los.CoverType)
                        {
                            case LosCalculations.CoverType.Full:
                                coverScore += 3f;
                                break;
                            case LosCalculations.CoverType.Half:
                                coverScore += 1.5f;
                                break;
                            case LosCalculations.CoverType.Invisible:
                                // v2.0.7: 시야선 없음 = 공격 불가 = 큰 감점
                                // 탱커/DPS는 적을 볼 수 있어야 함
                                if (role == UnitRole.Tank || role == UnitRole.DPS ||
                                    role == UnitRole.Hybrid || role == UnitRole.Balanced)
                                {
                                    coverScore -= 15f; // 공격 불가 - 매우 큰 패널티
                                }
                                else
                                {
                                    coverScore += 2f; // 서포터/스나이퍼는 숨는 것도 좋음
                                }
                                break;
                            case LosCalculations.CoverType.None:
                            default:
                                // v2.0.7: 시야선 있음 = 공격 가능 = 보너스
                                if (role == UnitRole.Tank || role == UnitRole.DPS ||
                                    role == UnitRole.Hybrid || role == UnitRole.Balanced)
                                {
                                    coverScore += 5f; // 공격 가능 보너스
                                }
                                break;
                        }
                    }
                    catch { }

                    // 2. 거리 점수 계산
                    try
                    {
                        int distance = WarhammerGeometryUtils.DistanceToInCells(
                            node.Vector3Position, unitSizeRect,
                            enemy.Entity.Position, enemyRect);

                        if (role == UnitRole.Tank || preferMelee)
                        {
                            // 탱커 또는 근접 선호: 가까울수록 좋음 (최대 점수 = 거리 0)
                            distanceScore += Mathf.Max(0, 10f - distance);
                        }
                        else
                        {
                            // 원거리: 이상적 거리에 가까울수록 좋음
                            float diff = Mathf.Abs(distance - idealDistance);
                            distanceScore += Mathf.Max(0, 10f - diff);
                        }
                    }
                    catch { }

                    // 3. 위협 점수 (적의 공격 범위 회피)
                    try
                    {
                        var enemyUnit = enemy.Entity as BaseUnitEntity;
                        if (enemyUnit != null)
                        {
                            var threatArea = enemyUnit.GetThreateningArea();
                            if (threatArea != null && threatArea.Contains(node))
                            {
                                threatScore -= 5f; // 위협 범위 안에 있으면 감점
                            }
                            else
                            {
                                threatScore += 1f; // 안전하면 가점
                            }
                        }
                    }
                    catch { }
                }

                // 정규화 및 가중 합산
                coverScore /= enemyCount;
                distanceScore /= enemyCount;
                threatScore /= enemyCount;

                float totalScore =
                    coverScore * coverWeight +
                    distanceScore * distanceWeight +
                    threatScore * threatWeight;

                return totalScore;
            }

            /// <summary>
            /// 유닛에서 가장 가까운 적까지의 거리
            /// </summary>
            static float GetMinDistanceToEnemies(BaseUnitEntity unit, List<TargetInfo> enemies)
            {
                float minDistance = float.MaxValue;

                foreach (var enemy in enemies)
                {
                    if (enemy?.Entity == null) continue;
                    try
                    {
                        int distance = WarhammerGeometryUtils.DistanceToInCells(
                            unit.Position, unit.SizeRect,
                            enemy.Entity.Position, enemy.Entity.SizeRect);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                    }
                    catch { }
                }

                return minDistance;
            }

            /// <summary>
            /// 특정 노드에서 가장 가까운 적까지의 거리
            /// </summary>
            static float GetMinDistanceToEnemiesFromNode(CustomGridNodeBase node, IntRect unitSize, List<TargetInfo> enemies)
            {
                float minDistance = float.MaxValue;

                foreach (var enemy in enemies)
                {
                    if (enemy?.Entity == null) continue;
                    try
                    {
                        int distance = WarhammerGeometryUtils.DistanceToInCells(
                            node.Vector3Position, unitSize,
                            enemy.Entity.Position, enemy.Entity.SizeRect);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                        }
                    }
                    catch { }
                }

                return minDistance;
            }
        }

        #endregion

        #region Movement Command Patch

        /// <summary>
        /// 이동 명령 패치 - 역할별로 이동 방향 결정
        /// - 탱커: ClosestEnemy로 강제 (SquadLeader 대신)
        /// - 스나이퍼/서포터: SquadLeader 유지
        /// </summary>
        [HarmonyPatch(typeof(TaskNodeSetupMoveCommand))]
        public static class SetupMoveCommandPatch
        {
            [HarmonyPatch("TickInternal")]
            [HarmonyPrefix]
            static bool Prefix(TaskNodeSetupMoveCommand __instance, Blackboard blackboard, ref Status __result)
            {
                try
                {
                    var context = blackboard?.DecisionContext;
                    if (context == null) return true;

                    var unit = context.Unit;
                    if (unit == null || !unit.IsDirectlyControllable) return true;

                    var settings = Main.Settings.GetOrCreateSettings(unit.UniqueId, unit.CharacterName);
                    if (!settings.EnableCustomAI) return true;

                    var role = GetRoleFromSettings(settings);

                    // m_Mode 필드 접근 (private)
                    var modeField = AccessTools.Field(typeof(TaskNodeSetupMoveCommand), "m_Mode");
                    if (modeField == null) return true;

                    var currentMode = (SetupMoveCommandMode)modeField.GetValue(__instance);

                    // 역할별 이동 로직
                    switch (role)
                    {
                        case UnitRole.Tank:
                            // 탱커: SquadLeader 대신 ClosestEnemy로 이동
                            if (currentMode == SetupMoveCommandMode.SquadLeader ||
                                currentMode == SetupMoveCommandMode.BetterPosition)
                            {
                                // 교전 중이 아니고 적이 있으면 적에게 돌진
                                bool isEngaged = unit.CombatState?.IsEngaged ?? false;
                                float hpPercent = GetHPPercent(unit);

                                // HP가 괜찮으면 적에게 돌진
                                if (hpPercent >= 30f && !isEngaged)
                                {
                                    modeField.SetValue(__instance, SetupMoveCommandMode.ClosestEnemy);
                                    Main.LogDebug($"[Movement] Tank {unit.CharacterName}: Changing {currentMode} -> ClosestEnemy");
                                }
                                // 교전 중이고 HP 괜찮으면 이동 안 함
                                else if (isEngaged && hpPercent >= 40f)
                                {
                                    __result = Status.Failure;
                                    Main.LogDebug($"[Movement] Tank {unit.CharacterName}: Holding position (engaged, HP: {hpPercent:F0}%)");
                                    return false;
                                }
                            }
                            break;

                        case UnitRole.DPS:
                            // DPS: PreferMelee면 ClosestEnemy, 아니면 기본
                            if (settings.RangePreference == Settings.RangePreference.PreferMelee)
                            {
                                // v2.0.9: BetterPosition도 포함 (Tank와 동일하게)
                                if (currentMode == SetupMoveCommandMode.SquadLeader ||
                                    currentMode == SetupMoveCommandMode.BetterPosition)
                                {
                                    bool isEngaged = unit.CombatState?.IsEngaged ?? false;
                                    float hpPercent = GetHPPercent(unit);

                                    // HP가 괜찮고 교전 중 아니면 적에게 돌진
                                    if (hpPercent >= 30f && !isEngaged)
                                    {
                                        modeField.SetValue(__instance, SetupMoveCommandMode.ClosestEnemy);
                                        Main.LogDebug($"[Movement] DPS {unit.CharacterName}: PreferMelee - Changing {currentMode} -> ClosestEnemy");
                                    }
                                }
                            }
                            break;

                        case UnitRole.Sniper:
                        case UnitRole.Support:
                            // 스나이퍼/서포터: 기본 로직 유지 (후방에서 지원)
                            break;

                        case UnitRole.Hybrid:
                        case UnitRole.Balanced:
                        default:
                            // 하이브리드/밸런스: 상황에 따라
                            break;
                    }

                    return true; // 원래 메서드 실행
                }
                catch (Exception ex)
                {
                    Main.LogError($"[Movement] SetupMoveCommand error: {ex}");
                    return true;
                }
            }
        }

        #endregion
    }
}
