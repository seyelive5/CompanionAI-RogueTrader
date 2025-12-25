using System;
using System.Collections.Generic;
using Kingmaker.UnitLogic.Abilities;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// ★ v2.2.33: 통합 능력 데이터베이스
    ///
    /// AbilityRules.cs + AbilityGuids.cs 통합
    /// - GUID 기반 정확한 스킬 식별 (다국어 호환)
    /// - 타이밍/규칙 정보 포함
    /// - 단일 진실 소스 (Single Source of Truth)
    /// </summary>
    public static class AbilityDatabase
    {
        #region Enums

        /// <summary>
        /// 스킬 사용 타이밍 분류
        /// </summary>
        public enum AbilityTiming
        {
            /// <summary>일반 스킬 - 언제든 사용 가능</summary>
            Normal,

            /// <summary>선제적 자기 버프 - 턴 시작 시 우선 사용</summary>
            PreCombatBuff,

            /// <summary>공격 직전 버프 - 공격 전에 사용하면 효과적</summary>
            PreAttackBuff,

            /// <summary>첫 행동 후 사용 - 추가 행동 활성화 (Run and Gun 등)</summary>
            PostFirstAction,

            /// <summary>턴 종료 스킬 - 턴 마지막에만 사용</summary>
            TurnEnding,

            /// <summary>마무리 스킬 - 적 HP 낮을 때만 효과적</summary>
            Finisher,

            /// <summary>자해 스킬 - HP를 소모하므로 HP 체크 필요</summary>
            SelfDamage,

            /// <summary>위험한 AoE - 아군 위치 확인 필수</summary>
            DangerousAoE,

            /// <summary>디버프 스킬 - 공격 전에 사용하면 효과적</summary>
            Debuff,

            /// <summary>Heroic Act - Momentum 175+ 필요</summary>
            HeroicAct,

            /// <summary>도발 스킬 - 근접 적 2명 이상일 때</summary>
            Taunt,

            /// <summary>재장전</summary>
            Reload,

            /// <summary>힐링/치료</summary>
            Healing,

            /// <summary>돌격/갭클로저</summary>
            GapCloser,

            /// <summary>긴급 상황 스킬 - HP 낮을 때</summary>
            Emergency,

            /// <summary>분노 스킬 - 적 처치 후 활성화</summary>
            RighteousFury,

            /// <summary>스택 버프</summary>
            StackingBuff,
        }

        /// <summary>
        /// 스킬 카테고리 (빠른 분류용)
        /// </summary>
        [Flags]
        public enum AbilityFlags
        {
            None = 0,
            SingleUse = 1 << 0,      // 전투당 1회
            RequiresLowHP = 1 << 1,  // 적 HP 낮을 때
            Dangerous = 1 << 2,      // 아군 피해 가능
        }

        #endregion

        #region Data Structure

        /// <summary>
        /// 능력 정보 구조체
        /// </summary>
        public readonly struct AbilityInfo
        {
            public readonly AbilityTiming Timing;
            public readonly float HPThreshold;      // 자해 스킬용 HP 임계값
            public readonly float TargetHPThreshold; // 마무리 스킬용 적 HP 임계값
            public readonly AbilityFlags Flags;
            public readonly string Description;

            public AbilityInfo(AbilityTiming timing, float hpThreshold = 0f, float targetHP = 0f,
                               AbilityFlags flags = AbilityFlags.None, string desc = "")
            {
                Timing = timing;
                HPThreshold = hpThreshold;
                TargetHPThreshold = targetHP;
                Flags = flags;
                Description = desc;
            }

            public bool IsSingleUse => (Flags & AbilityFlags.SingleUse) != 0;
            public bool IsDangerous => (Flags & AbilityFlags.Dangerous) != 0;
        }

        #endregion

        #region GUID Database

        /// <summary>
        /// GUID → AbilityInfo 매핑
        /// 모든 주요 스킬의 GUID와 규칙 정의
        /// </summary>
        private static readonly Dictionary<string, AbilityInfo> Database = new()
        {
            // ========================================
            // PostFirstAction - 첫 행동 후 사용
            // ========================================

            // Run and Gun
            { "22a25a3e418246ccbe95f2cc81c17473", new AbilityInfo(AbilityTiming.PostFirstAction, desc: "런 앤 건 - Heavy Bolter") },
            { "cfc7943b71f04a1c9be6465946fc9ee2", new AbilityInfo(AbilityTiming.PostFirstAction, desc: "런 앤 건 - Mob") },
            { "5e60764f84c94277ae6a78b63a1fd2aa", new AbilityInfo(AbilityTiming.PostFirstAction, desc: "런 앤 건 - Soldier") },

            // Daring Breach
            { "51366be5481b4ca7b348d9ac69a79f46", new AbilityInfo(AbilityTiming.PostFirstAction, 30f, desc: "대담한 돌파") },
            { "845a1ed417f2489489eab670b00b773a", new AbilityInfo(AbilityTiming.PostFirstAction, 30f, desc: "대담한 돌파 - Fighter") },
            { "ed21642647a14ead9a09183cd5318d11", new AbilityInfo(AbilityTiming.PostFirstAction, 30f, desc: "대담한 돌파 - Ultimate") },

            // ========================================
            // PreCombatBuff - 전투 시작 시 버프
            // ========================================

            // Defensive Stance
            { "cd42292391e74ba7809d0600ddb43a8d", new AbilityInfo(AbilityTiming.PreCombatBuff, desc: "방어 태세") },
            { "dfda4e8761d44549b0e70b10a71947fc", new AbilityInfo(AbilityTiming.PreCombatBuff, desc: "방어 태세 회복") },
            { "39247f7f6f024676a693a7e04fcc631d", new AbilityInfo(AbilityTiming.PreCombatBuff, desc: "방어 태세 - Vanguard") },

            // Bulwark
            { "0b693a158fed42a387d5f61ff6f0ae4c", new AbilityInfo(AbilityTiming.PreCombatBuff, desc: "방벽") },
            { "b064fd994f804996afc43725ffb75f7c", new AbilityInfo(AbilityTiming.PreCombatBuff, desc: "방벽 전략") },

            // ========================================
            // TurnEnding - 턴 종료 스킬
            // ========================================

            // Stalwart Defense
            { "f6a60b4556214528b0ce295c4f69306e", new AbilityInfo(AbilityTiming.TurnEnding, desc: "굳건한 방어") },

            // ========================================
            // Reload - 재장전
            // ========================================

            { "98f4a31b68e446ad9c63411c7b349146", new AbilityInfo(AbilityTiming.Reload, desc: "재장전") },
            { "b1704fc05eeb406ba23158061e765cac", new AbilityInfo(AbilityTiming.Reload, desc: "재장전 - 기회공격 무시") },
            { "121068f8b70641458b24b3edc31f9132", new AbilityInfo(AbilityTiming.Reload, desc: "플라즈마 재장전") },
            { "1e3a9caa44f04f7696ad5bd4ec4056a3", new AbilityInfo(AbilityTiming.Reload, desc: "켈러모프 재장전") },
            { "1cedb5f0cf104f57a88f91168e4c0df8", new AbilityInfo(AbilityTiming.Reload, desc: "전투 후 재장전") },

            // ========================================
            // Taunt - 도발
            // ========================================

            { "742ab23861c544b38f26e17175d17183", new AbilityInfo(AbilityTiming.Taunt, desc: "도발") },
            { "46e7a840c3d04703b154660efb45538b", new AbilityInfo(AbilityTiming.Taunt, desc: "도발 - Vanguard") },
            { "a8c7d8404d104d4dad2d460ec2b470ee", new AbilityInfo(AbilityTiming.Taunt, desc: "도발 신호 - Servoskull") },
            { "13e41af1d54c458da81050336ce8e0fc", new AbilityInfo(AbilityTiming.Taunt, desc: "조롱하는 외침") },
            { "383d89aaa52f4c3f8e19a02659ce19e7", new AbilityInfo(AbilityTiming.Taunt, desc: "선동가의 투구") },

            // ========================================
            // Finisher - 마무리
            // ========================================

            { "6a4c3b65dff840e0aab5966ffe8aa7ba", new AbilityInfo(AbilityTiming.Finisher, targetHP: 30f, desc: "사형 선고") },
            { "5b8545bc7a90491d865410a585071efe", new AbilityInfo(AbilityTiming.Finisher, targetHP: 30f, desc: "임무 완수") },
            { "cf6c3356a9b44dd7badea16a625687be", new AbilityInfo(AbilityTiming.Finisher, targetHP: 30f, desc: "임무 완수 - 보조") },
            { "ed10346264414140936abd17d6c5b445", new AbilityInfo(AbilityTiming.Finisher, targetHP: 25f, desc: "해체의 황혼") },
            { "614fe492067d4b50b03695782af00f00", new AbilityInfo(AbilityTiming.Finisher, targetHP: 30f, desc: "파워몰 마무리") },
            { "70b2fcb4c67544da847ea8e0792191d5", new AbilityInfo(AbilityTiming.Finisher, targetHP: 30f, desc: "즉각 처형 - Desperate") },

            // ========================================
            // HeroicAct - Momentum 175+
            // ========================================

            { "635161f3087c4294bf39c5fefe3d01af", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "연쇄 번개") },
            { "fda0e6fc865d4712a8dd48a63bce326e", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "너글의 선물") },
            { "234425ce980548588fc9bb0fbd08497b", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "데이터 축복") },
            { "ac688f9b6e8443da8380431780785eb8", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "정밀 조정") },
            { "1497623133f74bcabd797aecdab2bb05", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "즉각 처형 - Heroic") },
            { "8dfbb8da5a3b4a5b83e45934661fdd82", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "식인") },
            { "bba2c59af522402f8a6c2690256b1f8e", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "끝없는 학살") },
            { "1a0e3b0471da4d61be248f36eac5fdaa", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "광란") },
            { "7a05cb34622f47fb8e704cebbfab3df8", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "발화탄") },
            { "189ed32cd3c746078d63bd98c58ef05f", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "고통 보복") },
            { "8f34b8e6b92e48e09d411e34de5e5462", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "빠른 공격") },
            { "49977f6a6b414182bb6af1f130073981", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "젠취 공포") },
            { "741837887a4f429193ba44ad3948a71a", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "과다복용") },
            { "f8629a743eaf414eb8bf79edee9b02d0", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "생명력 흡수") },
            { "14c15a3b56d54b30addcc1df8f4a6420", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "생명력 흡수 - Sculptor") },
            { "1598fb26e644442098e72562537ae660", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "사형 선고 - Divination") },
            { "84ddefd28f224d5fb3f5e176375c1f05", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "인페르노") },
            { "bdde427505b14cd68b20ab0d915d5fe3", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "황제의 분노") },
            { "9edd0e95bfea4532b764920a7b7f67bf", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "속박") },
            { "5b19d80b3d694f77b84c2b38a04efe8f", new AbilityInfo(AbilityTiming.HeroicAct, flags: AbilityFlags.SingleUse, desc: "죽음의 환영") },

            // ========================================
            // Healing - 치료
            // ========================================

            { "083d5280759b4ed3a2d0b61254653273", new AbilityInfo(AbilityTiming.Healing, desc: "메디킷") },
            { "b6e3c9398ea94c75afdbf61633ce2f85", new AbilityInfo(AbilityTiming.Healing, desc: "메디킷 - Battle Medic") },
            { "dd2e9a6170b448d4b2ec5a7fe0321e65", new AbilityInfo(AbilityTiming.Healing, desc: "전투 자극제 메디킷") },
            { "ededbc48a7f24738a0fdb708fc48bb4c", new AbilityInfo(AbilityTiming.Healing, desc: "군의관 메디킷") },
            { "1359b77cf6714555895e2d3577f6f9b9", new AbilityInfo(AbilityTiming.Healing, desc: "검투사 치료 키트") },
            { "48ac9afb9b6d4caf8d488ea85d3d60ac", new AbilityInfo(AbilityTiming.Healing, desc: "피부 패치") },
            { "2e9a23383b574408b4acdf6b62f6ed9b", new AbilityInfo(AbilityTiming.Healing, desc: "레이버 메디킷") },
            { "2081944e0fd8481e84c30ec03cfdc04e", new AbilityInfo(AbilityTiming.Healing, desc: "적절한 치료") },
            { "d722bfac662c40f9b2a47dc6ea70d00a", new AbilityInfo(AbilityTiming.Healing, desc: "대형 메디킷") },
            { "0a88bfaa16ff41ea847ea14f58b384da", new AbilityInfo(AbilityTiming.Healing, desc: "외상 치료") },

            // ========================================
            // SelfDamage - 자해 스킬
            // ========================================

            { "590c990c1d684fd09ae883754d28a8ac", new AbilityInfo(AbilityTiming.SelfDamage, 60f, flags: AbilityFlags.SingleUse, desc: "피의 맹세") },
            { "858e841542554025bc3ecdb6336b87ea", new AbilityInfo(AbilityTiming.SelfDamage, 50f, desc: "피 흘리기") },
            { "566b140329b3441aafa971d729124947", new AbilityInfo(AbilityTiming.SelfDamage, 70f, desc: "무모한 결단") },
            { "29b7ab2d3e2640f3ad20a5c44c300346", new AbilityInfo(AbilityTiming.SelfDamage, 80f, flags: AbilityFlags.SingleUse, desc: "과도 대사 촉진") },

            // ========================================
            // DangerousAoE - 위험한 AoE
            // ========================================

            { "1a508a8b705e427aa00dcae2bdba407e", new AbilityInfo(AbilityTiming.DangerousAoE, flags: AbilityFlags.Dangerous, desc: "눈꺼풀 없는 응시 - Mob") },
            { "b932b545f5d8460ab562b0003294e775", new AbilityInfo(AbilityTiming.DangerousAoE, flags: AbilityFlags.Dangerous, desc: "눈꺼풀 없는 응시") },
            { "b79546d74c044b2e936362536656ab6f", new AbilityInfo(AbilityTiming.DangerousAoE, flags: AbilityFlags.Dangerous, desc: "칼날 춤 - Master") },
            { "e955823f54d24088ae1fdefe88d3684d", new AbilityInfo(AbilityTiming.DangerousAoE, flags: AbilityFlags.Dangerous, desc: "칼날 춤 - Reaper") },

            // ========================================
            // Debuff - 디버프
            // ========================================

            { "197b8a8a12b0442db7ffee1067cf3d97", new AbilityInfo(AbilityTiming.Debuff, desc: "약점 노출") },
            { "c8b1b420e52c46699781bf2789e9905c", new AbilityInfo(AbilityTiming.Debuff, desc: "약점 노출 - Sub") },
            { "91d40472299d48ffa675c249c4226d64", new AbilityInfo(AbilityTiming.Debuff, desc: "목표 지정") },

            // ========================================
            // GapCloser - 갭 클로저
            // ========================================

            { "c78506dd0e14f7c45a599990e4e65038", new AbilityInfo(AbilityTiming.GapCloser, desc: "돌격") },
            { "40800d54d3d64c7cb2d746cc2cce9a1b", new AbilityInfo(AbilityTiming.GapCloser, desc: "돌격 - CSM") },
            { "67f785ba0562480697aa3735bdd9e0c2", new AbilityInfo(AbilityTiming.GapCloser, desc: "돌격 - Uralon") },
            { "4955b43454f6488f82892e166c76c995", new AbilityInfo(AbilityTiming.GapCloser, desc: "돌격 - Fighter") },
            { "d9f20b396eb64a4293c9e3bd3270e0dc", new AbilityInfo(AbilityTiming.GapCloser, desc: "대적할수 없는 맹공") },
            { "8fed5098066b48efa1e09a14f7b8f6c6", new AbilityInfo(AbilityTiming.GapCloser, desc: "급습 - Ambull") },
            { "c3e407372e02483e87b350235fc409f0", new AbilityInfo(AbilityTiming.GapCloser, desc: "급습 텔레포트 - Ambull") },
        };

        #endregion

        #region Core Methods

        /// <summary>
        /// 능력의 GUID 추출
        /// </summary>
        public static string GetGuid(AbilityData ability)
        {
            try
            {
                return ability?.Blueprint?.AssetGuid?.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// GUID로 능력 정보 조회
        /// </summary>
        public static AbilityInfo? GetInfo(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            return Database.TryGetValue(guid, out var info) ? info : null;
        }

        /// <summary>
        /// 능력 데이터로 정보 조회
        /// </summary>
        public static AbilityInfo? GetInfo(AbilityData ability)
        {
            string guid = GetGuid(ability);
            return GetInfo(guid);
        }

        /// <summary>
        /// 능력의 타이밍 조회 (미등록 시 자동 감지)
        /// </summary>
        public static AbilityTiming GetTiming(AbilityData ability)
        {
            var info = GetInfo(ability);
            if (info.HasValue) return info.Value.Timing;

            // 미등록 스킬: 자동 감지
            return AutoDetectTiming(ability);
        }

        /// <summary>
        /// 스킬 속성 기반 자동 타이밍 감지
        /// </summary>
        private static AbilityTiming AutoDetectTiming(AbilityData ability)
        {
            try
            {
                var bp = ability?.Blueprint;
                if (bp == null) return AbilityTiming.Normal;

                bool canTargetSelf = bp.CanTargetSelf;
                bool canTargetEnemies = bp.CanTargetEnemies;
                bool canTargetFriends = bp.CanTargetFriends;
                bool hasWeapon = ability.Weapon != null;
                string range = bp.Range.ToString();
                string bpName = bp.name?.ToLower() ?? "";

                // 위험한 AoE (적과 아군 모두 타겟 가능)
                if (canTargetEnemies && canTargetFriends && !canTargetSelf)
                    return AbilityTiming.DangerousAoE;

                // Personal 자기 버프
                if (range == "Personal" && canTargetSelf && !canTargetEnemies && !hasWeapon)
                {
                    if (bpName.Contains("veil") || bpName.Contains("stance") ||
                        bpName.Contains("defend") || bpName.Contains("guard"))
                        return AbilityTiming.TurnEnding;

                    return AbilityTiming.PreAttackBuff;
                }

                // 자해 스킬 감지
                if (bpName.Contains("blood") || bpName.Contains("oath") ||
                    bpName.Contains("sacrifice") || bpName.Contains("wound"))
                {
                    if (canTargetSelf || range == "Personal")
                        return AbilityTiming.SelfDamage;
                }

                // 마무리 스킬 감지
                if (bpName.Contains("dispatch") || bpName.Contains("execute") ||
                    bpName.Contains("finish") || bpName.Contains("deathblow"))
                    return AbilityTiming.Finisher;

                return AbilityTiming.Normal;
            }
            catch
            {
                return AbilityTiming.Normal;
            }
        }

        #endregion

        #region Category Check Methods

        /// <summary>Run and Gun 계열인지 확인</summary>
        public static bool IsRunAndGun(AbilityData ability)
        {
            var info = GetInfo(ability);
            if (!info.HasValue) return false;

            // PostFirstAction이면서 HP 임계값 없는 것 = Run and Gun
            return info.Value.Timing == AbilityTiming.PostFirstAction && info.Value.HPThreshold == 0f;
        }

        /// <summary>PostFirstAction 스킬인지 확인</summary>
        public static bool IsPostFirstAction(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.PostFirstAction;
        }

        /// <summary>재장전 스킬인지 확인</summary>
        public static bool IsReload(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.Reload;
        }

        /// <summary>도발 스킬인지 확인</summary>
        public static bool IsTaunt(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.Taunt;
        }

        /// <summary>마무리 스킬인지 확인</summary>
        public static bool IsFinisher(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.Finisher;
        }

        /// <summary>Heroic Act 스킬인지 확인</summary>
        public static bool IsHeroicAct(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.HeroicAct;
        }

        /// <summary>힐링 스킬인지 확인</summary>
        public static bool IsHealing(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.Healing;
        }

        /// <summary>자해 스킬인지 확인</summary>
        public static bool IsSelfDamage(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.SelfDamage;
        }

        /// <summary>위험한 AoE 스킬인지 확인</summary>
        public static bool IsDangerousAoE(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.DangerousAoE;
        }

        /// <summary>디버프 스킬인지 확인</summary>
        public static bool IsDebuff(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.Debuff;
        }

        /// <summary>갭 클로저 스킬인지 확인</summary>
        public static bool IsGapCloser(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.GapCloser;
        }

        /// <summary>방어 태세 스킬인지 확인</summary>
        public static bool IsDefensiveStance(AbilityData ability)
        {
            var info = GetInfo(ability);
            if (!info.HasValue) return false;

            return info.Value.Timing == AbilityTiming.PreCombatBuff &&
                   info.Value.Description.Contains("방어 태세");
        }

        /// <summary>돌격 스킬인지 확인</summary>
        public static bool IsCharge(AbilityData ability)
        {
            return IsGapCloser(ability);
        }

        #endregion

        #region Rule Check Methods

        /// <summary>선제적 버프인지 확인</summary>
        public static bool IsProactiveBuff(AbilityData ability)
        {
            var timing = GetTiming(ability);
            return timing == AbilityTiming.PreCombatBuff || timing == AbilityTiming.PreAttackBuff;
        }

        /// <summary>턴 종료 스킬인지 확인</summary>
        public static bool IsTurnEnding(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.TurnEnding;
        }

        /// <summary>공격 외 타이밍 스킬인지 확인 (필터링용)</summary>
        public static bool IsNonAttackTiming(AbilityData ability)
        {
            var timing = GetTiming(ability);
            return timing == AbilityTiming.Debuff ||
                   timing == AbilityTiming.PreCombatBuff ||
                   timing == AbilityTiming.PreAttackBuff ||
                   timing == AbilityTiming.TurnEnding ||
                   timing == AbilityTiming.HeroicAct ||
                   timing == AbilityTiming.Taunt ||
                   timing == AbilityTiming.Healing;
        }

        /// <summary>HP 임계값 조회 (자해 스킬용)</summary>
        public static float GetHPThreshold(AbilityData ability)
        {
            var info = GetInfo(ability);
            return info?.HPThreshold ?? 0f;
        }

        /// <summary>적 HP 임계값 조회 (마무리 스킬용)</summary>
        public static float GetTargetHPThreshold(AbilityData ability)
        {
            var info = GetInfo(ability);
            return info?.TargetHPThreshold ?? 30f;  // 기본값 30%
        }

        /// <summary>전투당 1회 사용 스킬인지 확인</summary>
        public static bool IsSingleUse(AbilityData ability)
        {
            var info = GetInfo(ability);
            return info?.IsSingleUse ?? false;
        }

        /// <summary>Righteous Fury 스킬인지 확인</summary>
        public static bool IsRighteousFury(AbilityData ability)
        {
            return GetTiming(ability) == AbilityTiming.RighteousFury;
        }

        /// <summary>
        /// ★ v2.2.33: 하위 호환성을 위한 GetRule 래퍼
        /// GetInfo()와 동일하게 동작
        /// </summary>
        public static AbilityInfo? GetRule(AbilityData ability)
        {
            return GetInfo(ability);
        }

        #endregion
    }
}
