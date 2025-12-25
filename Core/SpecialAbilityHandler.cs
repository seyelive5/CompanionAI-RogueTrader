using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Buffs;
using UnityEngine;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.29: 특수 능력 처리 시스템
    ///
    /// 게임의 ContextActionIntensifyDOT, AbilityDeliverChain 등 패턴 참고
    ///
    /// 지원하는 특수 능력 유형:
    /// 1. DoT 강화 (Symphony of Flames 등) - 타겟에 기존 DoT 있을 때만 효과적
    /// 2. 연쇄 효과 (Chain Lightning 등) - 다수 타겟 있을 때 효과적
    /// 3. 콤보 후속 - 선행 스킬 사용 후 효과 증가
    /// 4. 버프 기반 - 특정 버프 있을 때만 사용 가능
    /// 5. 디버프 강화 - 적에게 디버프 있을 때 추가 효과
    /// </summary>
    public static class SpecialAbilityHandler
    {
        #region DOT Types (from game's DOT enum)

        /// <summary>
        /// 게임의 DOT 타입 (Kingmaker.Enums.DOT 참고)
        /// </summary>
        public enum DOTType
        {
            Bleeding,           // 출혈
            Burning,            // 화염
            Toxic,              // 독
            PsykerBurning,      // 사이커 화염
            NavigatorBurning,   // 내비게이터 화염
            PsykerToxin,        // 사이커 독
            AssassinHaemorrhage // 암살자 출혈
        }

        #endregion

        #region Special Ability Types

        /// <summary>
        /// 특수 능력 카테고리
        /// </summary>
        public enum SpecialAbilityType
        {
            /// <summary>일반 능력 - 특수 처리 불필요</summary>
            None,

            /// <summary>DoT 강화 - 타겟에 기존 DoT 있을 때 효과 증가</summary>
            DOTIntensify,

            /// <summary>연쇄 효과 - 타겟 간 전파</summary>
            ChainEffect,

            /// <summary>콤보 후속 - 선행 스킬 후 효과 증가</summary>
            ComboFollowup,

            /// <summary>버프 필요 - 특정 버프 활성화 필요</summary>
            BuffRequired,

            /// <summary>디버프 강화 - 적 디버프 시 추가 효과</summary>
            DebuffEnhancer,

            /// <summary>스택 기반 - 스택에 따라 효과 증가</summary>
            StackBased
        }

        #endregion

        #region Special Ability GUID Registry

        /// <summary>
        /// DoT 강화 능력 GUID (Symphony of Flames 등)
        /// 기존 DoT가 있을 때 효과가 증폭됨
        /// </summary>
        public static readonly HashSet<string> DOTIntensifyAbilities = new HashSet<string>
        {
            // ★ Pyromancy - Shape Flames / 불꽃의 교향곡
            // 기존 Burning DoT가 있을 때 피해 증폭
            "7720d74e51f94184bb43b97ce9c9e53f",  // Pyromancy_ShapeFlames_Ability - 불꽃의 교향곡

            // Fan the Flames / 불태워라 - 화염 DoT 증폭
            "24f1e49a2294434da2dc17edb6808517",  // Pyromancy_FanTheFlames_Ability - 불태워라
            "cb3a7a2b865d424183d290b4ff8d3f34",  // Pyromancy_FanTheFlames_EnemiesOnly_MobAbility - 축성된 불꽃
        };

        /// <summary>
        /// 연쇄 효과 능력 GUID (Chain Lightning 등)
        /// 다수의 적이 있을 때 효과적
        /// </summary>
        public static readonly HashSet<string> ChainEffectAbilities = new HashSet<string>
        {
            // Chain Lightning / 연쇄 번개
            "635161f3087c4294bf39c5fefe3d01af",  // ChainLightningPush_Ability - 연쇄 번개 (영웅적 행위)
            "7b68b4aa3c024f348a20dce3ef172e40",  // ChainLightning_Ability - 연쇄 번개 (기본)
            "3c48374cbe244fc2bb8b6293230a6829",  // ChainLightningDesperatePush_Ability - 연쇄 번개 (필사적인 수단)

            // Arc Rifle 연쇄
            // Tesla Chain 등 - 추후 추가
        };

        /// <summary>
        /// 디버프 강화 능력 GUID
        /// 적에게 특정 디버프가 있을 때 추가 효과
        /// </summary>
        public static readonly HashSet<string> DebuffEnhancerAbilities = new HashSet<string>
        {
            // 추후 추가 예정
        };

        /// <summary>
        /// Burning DoT 적용 능력 (Shape Flames와 콤보)
        /// </summary>
        public static readonly HashSet<string> BurningDOTAbilities = new HashSet<string>
        {
            // Inferno / 인페르노 - Burning DoT 적용
            "8a759cdc2b754309b1fb75397798fbf1",  // Pyromancy_Weapon_Inferno_Ability - 인페르노
            "c4ea2ad9fe1e4509916cb5f1787b1530",  // Pyromancy_Weapon_Inferno_Desperate_Ability - 인페르노 (필사적인 수단)
            "84ddefd28f224d5fb3f5e176375c1f05",  // Pyromancy_Weapon_Inferno_Heroic_Ability - 인페르노 (영웅적 행위)

            // Fire Storm / 화염 폭풍
            "321a9274e3454d69ada142f4ce540b12",  // Pyromancy_FireStorm_Ability - 화염 폭풍
        };

        #endregion

        #region DOT-related Keywords for Detection

        /// <summary>
        /// 화염 DoT 관련 블루프린트 이름 패턴
        /// </summary>
        private static readonly string[] BurningDOTPatterns = new[]
        {
            "burning", "burn", "flame", "fire", "inferno", "immolat", "pyro",
            "화염", "불꽃", "연소"
        };

        /// <summary>
        /// 출혈 DoT 관련 블루프린트 이름 패턴
        /// </summary>
        private static readonly string[] BleedingDOTPatterns = new[]
        {
            "bleed", "haemorrhage", "hemorrhage", "blood", "wound",
            "출혈", "피"
        };

        /// <summary>
        /// 독 DoT 관련 블루프린트 이름 패턴
        /// </summary>
        private static readonly string[] ToxicDOTPatterns = new[]
        {
            "toxic", "poison", "venom", "blight",
            "독", "중독"
        };

        #endregion

        #region Main API

        /// <summary>
        /// 능력이 특수 처리 필요한지 확인
        /// </summary>
        public static bool IsSpecialAbility(AbilityData ability)
        {
            return GetSpecialType(ability) != SpecialAbilityType.None;
        }

        /// <summary>
        /// 능력의 특수 타입 가져오기
        /// </summary>
        public static SpecialAbilityType GetSpecialType(AbilityData ability)
        {
            if (ability == null) return SpecialAbilityType.None;

            string guid = AbilityDatabase.GetGuid(ability);

            // GUID 기반 우선 확인
            if (!string.IsNullOrEmpty(guid))
            {
                if (DOTIntensifyAbilities.Contains(guid)) return SpecialAbilityType.DOTIntensify;
                if (ChainEffectAbilities.Contains(guid)) return SpecialAbilityType.ChainEffect;
                if (DebuffEnhancerAbilities.Contains(guid)) return SpecialAbilityType.DebuffEnhancer;
            }

            // 블루프린트 이름 기반 폴백 감지
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            // DoT 강화 스킬 감지 (Symphony, Intensify 등)
            if (bpName.Contains("symphony") || bpName.Contains("intensify") ||
                bpName.Contains("교향곡") || bpName.Contains("강화"))
            {
                // 화염 관련이면 DoT 강화
                if (ContainsAny(bpName, BurningDOTPatterns))
                    return SpecialAbilityType.DOTIntensify;
            }

            // 연쇄 효과 감지
            if (bpName.Contains("chain") || bpName.Contains("연쇄") || bpName.Contains("arc"))
            {
                return SpecialAbilityType.ChainEffect;
            }

            // 콤보 후속 감지
            if (bpName.Contains("followup") || bpName.Contains("combo") ||
                bpName.Contains("후속") || bpName.Contains("연계"))
            {
                return SpecialAbilityType.ComboFollowup;
            }

            return SpecialAbilityType.None;
        }

        /// <summary>
        /// 특수 능력을 현재 상황에서 사용할 수 있는지 (효과적인지) 확인
        /// </summary>
        public static bool CanUseSpecialAbilityEffectively(AbilityData ability, ActionContext ctx, BaseUnitEntity target)
        {
            if (ability == null || target == null) return false;

            var specialType = GetSpecialType(ability);

            switch (specialType)
            {
                case SpecialAbilityType.None:
                    return true; // 일반 능력은 항상 OK

                case SpecialAbilityType.DOTIntensify:
                    // 타겟에 DoT가 있어야 효과적
                    var dotType = InferDOTTypeFromAbility(ability);
                    bool hasDoT = HasDoT(target, dotType);

                    if (!hasDoT)
                    {
                        Main.LogDebug($"[SpecialAbility] {ability.Name} skipped - target has no {dotType} DoT");
                        return false;
                    }
                    Main.Log($"[SpecialAbility] {ability.Name} effective - target has {dotType} DoT!");
                    return true;

                case SpecialAbilityType.ChainEffect:
                    // 최소 2명 이상의 적이 있어야 효과적
                    int chainTargets = CountChainTargets(ability, target, ctx.Enemies);
                    if (chainTargets < 2)
                    {
                        Main.LogDebug($"[SpecialAbility] {ability.Name} skipped - only {chainTargets} chain target(s)");
                        return false;
                    }
                    Main.Log($"[SpecialAbility] {ability.Name} effective - {chainTargets} chain targets!");
                    return true;

                case SpecialAbilityType.DebuffEnhancer:
                    // 타겟에 디버프가 있어야 효과적
                    bool hasDebuff = HasDebuff(target);
                    if (!hasDebuff)
                    {
                        Main.LogDebug($"[SpecialAbility] {ability.Name} skipped - target has no debuff");
                        return false;
                    }
                    return true;

                case SpecialAbilityType.ComboFollowup:
                    // TODO: 선행 스킬 사용 여부 확인
                    return true;

                case SpecialAbilityType.BuffRequired:
                    // TODO: 필요 버프 확인
                    return true;

                default:
                    return true;
            }
        }

        /// <summary>
        /// 특수 능력의 효과 점수 계산 (0-100)
        /// 높을수록 사용 권장
        /// </summary>
        public static int GetSpecialAbilityEffectivenessScore(AbilityData ability, ActionContext ctx, BaseUnitEntity target)
        {
            if (ability == null || target == null) return 0;

            var specialType = GetSpecialType(ability);

            switch (specialType)
            {
                case SpecialAbilityType.DOTIntensify:
                    // DoT 강도에 따라 점수
                    var dotType = InferDOTTypeFromAbility(ability);
                    int dotStacks = CountDOTStacks(target, dotType);
                    if (dotStacks == 0) return 0;
                    return Math.Min(100, 50 + dotStacks * 10); // 기본 50 + 스택당 10

                case SpecialAbilityType.ChainEffect:
                    // 연쇄 타겟 수에 따라 점수
                    int chainCount = CountChainTargets(ability, target, ctx.Enemies);
                    if (chainCount < 2) return 20; // 1명만이면 낮음
                    return Math.Min(100, chainCount * 25); // 타겟당 25점

                case SpecialAbilityType.DebuffEnhancer:
                    // 디버프 수에 따라 점수
                    int debuffCount = CountDebuffs(target);
                    return Math.Min(100, 40 + debuffCount * 15);

                default:
                    return 50; // 기본값
            }
        }

        #endregion

        #region DOT Detection

        /// <summary>
        /// 타겟에 DoT가 있는지 확인
        /// 게임의 PartDOTDirector.m_DOTs 참조
        /// </summary>
        public static bool HasDoT(BaseUnitEntity target, DOTType? specificType = null)
        {
            if (target == null) return false;

            try
            {
                // 방법 1: 버프에서 DoT 관련 키워드 검색
                foreach (var buff in target.Buffs.Enumerable)
                {
                    string buffName = buff.Blueprint?.name?.ToLower() ?? "";

                    if (specificType.HasValue)
                    {
                        // 특정 타입만 확인
                        if (IsDOTBuff(buffName, specificType.Value))
                            return true;
                    }
                    else
                    {
                        // 모든 DoT 타입 확인
                        if (IsAnyDOTBuff(buffName))
                            return true;
                    }
                }

                // 방법 2: PartDOTDirector 직접 접근 시도
                try
                {
                    var dotDirector = target.GetOptional<Kingmaker.UnitLogic.Buffs.Components.DOTLogic.PartDOTDirector>();
                    if (dotDirector != null)
                    {
                        // DOTDirector가 있다는 것은 DoT가 활성화됐다는 의미
                        return true;
                    }
                }
                catch
                {
                    // PartDOTDirector 접근 실패 시 버프 기반으로만 판단
                }

                return false;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[SpecialAbility] HasDoT error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// DoT 스택 수 계산
        /// </summary>
        public static int CountDOTStacks(BaseUnitEntity target, DOTType? specificType = null)
        {
            if (target == null) return 0;

            int count = 0;

            try
            {
                foreach (var buff in target.Buffs.Enumerable)
                {
                    string buffName = buff.Blueprint?.name?.ToLower() ?? "";

                    if (specificType.HasValue)
                    {
                        if (IsDOTBuff(buffName, specificType.Value))
                            count++;
                    }
                    else
                    {
                        if (IsAnyDOTBuff(buffName))
                            count++;
                    }
                }
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[SpecialAbility] CountDOTStacks error: {ex.Message}");
            }

            return count;
        }

        /// <summary>
        /// 버프 이름이 특정 DoT 타입과 일치하는지 확인
        /// </summary>
        private static bool IsDOTBuff(string buffName, DOTType dotType)
        {
            switch (dotType)
            {
                case DOTType.Burning:
                case DOTType.PsykerBurning:
                case DOTType.NavigatorBurning:
                    return ContainsAny(buffName, BurningDOTPatterns);

                case DOTType.Bleeding:
                case DOTType.AssassinHaemorrhage:
                    return ContainsAny(buffName, BleedingDOTPatterns);

                case DOTType.Toxic:
                case DOTType.PsykerToxin:
                    return ContainsAny(buffName, ToxicDOTPatterns);

                default:
                    return false;
            }
        }

        /// <summary>
        /// 버프 이름이 어떤 DoT 타입이든 일치하는지 확인
        /// </summary>
        private static bool IsAnyDOTBuff(string buffName)
        {
            return ContainsAny(buffName, BurningDOTPatterns) ||
                   ContainsAny(buffName, BleedingDOTPatterns) ||
                   ContainsAny(buffName, ToxicDOTPatterns);
        }

        /// <summary>
        /// 능력에서 관련 DoT 타입 추론
        /// </summary>
        private static DOTType? InferDOTTypeFromAbility(AbilityData ability)
        {
            if (ability == null) return null;

            string bpName = ability.Blueprint?.name?.ToLower() ?? "";

            if (ContainsAny(bpName, BurningDOTPatterns))
                return DOTType.Burning;

            if (ContainsAny(bpName, BleedingDOTPatterns))
                return DOTType.Bleeding;

            if (ContainsAny(bpName, ToxicDOTPatterns))
                return DOTType.Toxic;

            return null;
        }

        #endregion

        #region Chain Effect Detection

        /// <summary>
        /// 연쇄 효과용 유효 타겟 수 계산
        /// 게임의 AbilityDeliverChain.SelectNextTarget 참조
        /// </summary>
        public static int CountChainTargets(AbilityData ability, BaseUnitEntity initialTarget, List<BaseUnitEntity> enemies)
        {
            if (ability == null || initialTarget == null || enemies == null) return 0;

            // 기본 연쇄 범위 (게임 기본값)
            float chainRadius = GetChainRadius(ability);
            int maxChainTargets = GetMaxChainTargets(ability);

            // 초기 타겟 포함
            int count = 1;
            var usedTargets = new HashSet<BaseUnitEntity> { initialTarget };
            Vector3 currentPosition = initialTarget.Position;

            // 연쇄 가능한 타겟 계산
            for (int i = 0; i < maxChainTargets - 1; i++)
            {
                BaseUnitEntity nextTarget = null;
                float closestDistance = float.MaxValue;

                foreach (var enemy in enemies)
                {
                    if (enemy == null || enemy.LifeState.IsDead) continue;
                    if (usedTargets.Contains(enemy)) continue;

                    float distance = Vector3.Distance(currentPosition, enemy.Position);
                    if (distance <= chainRadius && distance < closestDistance)
                    {
                        closestDistance = distance;
                        nextTarget = enemy;
                    }
                }

                if (nextTarget == null) break;

                count++;
                usedTargets.Add(nextTarget);
                currentPosition = nextTarget.Position;
            }

            return count;
        }

        /// <summary>
        /// 능력의 연쇄 범위 가져오기
        /// </summary>
        private static float GetChainRadius(AbilityData ability)
        {
            // 기본값: 7m (게임의 일반적인 연쇄 범위)
            // TODO: 블루프린트에서 실제 값 추출
            return 7f;
        }

        /// <summary>
        /// 능력의 최대 연쇄 타겟 수 가져오기
        /// </summary>
        private static int GetMaxChainTargets(AbilityData ability)
        {
            // 기본값: 5 (일반적인 연쇄 스킬)
            // TODO: 블루프린트에서 실제 값 추출
            return 5;
        }

        #endregion

        #region Debuff Detection

        /// <summary>
        /// 타겟에 디버프가 있는지 확인
        /// (이름 기반 검사 - Harmful 속성 없음)
        /// </summary>
        public static bool HasDebuff(BaseUnitEntity target)
        {
            if (target == null) return false;

            try
            {
                foreach (var buff in target.Buffs.Enumerable)
                {
                    string buffName = buff.Blueprint?.name?.ToLower() ?? "";
                    if (IsDebuffByName(buffName))
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
        /// 타겟의 디버프 수 계산
        /// </summary>
        public static int CountDebuffs(BaseUnitEntity target)
        {
            if (target == null) return 0;

            int count = 0;

            try
            {
                foreach (var buff in target.Buffs.Enumerable)
                {
                    string buffName = buff.Blueprint?.name?.ToLower() ?? "";
                    if (IsDebuffByName(buffName))
                        count++;
                }
            }
            catch { }

            return count;
        }

        /// <summary>
        /// 버프 이름으로 디버프 여부 판단
        /// </summary>
        private static bool IsDebuffByName(string buffName)
        {
            return buffName.Contains("weaken") || buffName.Contains("slow") ||
                   buffName.Contains("stun") || buffName.Contains("blind") ||
                   buffName.Contains("fear") || buffName.Contains("vulnerability") ||
                   buffName.Contains("expose") || buffName.Contains("mark") ||
                   IsAnyDOTBuff(buffName);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// 문자열이 패턴 배열 중 하나라도 포함하는지 확인
        /// </summary>
        private static bool ContainsAny(string text, string[] patterns)
        {
            if (string.IsNullOrEmpty(text)) return false;

            foreach (var pattern in patterns)
            {
                if (text.Contains(pattern))
                    return true;
            }

            return false;
        }

        #endregion

        #region Combo Detection

        /// <summary>
        /// 능력이 Burning DoT를 적용하는지 확인
        /// (Shape Flames 콤보용)
        /// </summary>
        public static bool AppliesBurningDOT(AbilityData ability)
        {
            if (ability == null) return false;

            string guid = AbilityDatabase.GetGuid(ability);
            if (!string.IsNullOrEmpty(guid) && BurningDOTAbilities.Contains(guid))
                return true;

            // 이름 기반 폴백
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            return bpName.Contains("inferno") || bpName.Contains("firestorm") ||
                   bpName.Contains("인페르노") || bpName.Contains("화염폭풍");
        }

        /// <summary>
        /// DoT 강화 능력인지 확인 (Symphony of Flames 등)
        /// </summary>
        public static bool IsDOTIntensifyAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string guid = AbilityDatabase.GetGuid(ability);
            if (!string.IsNullOrEmpty(guid) && DOTIntensifyAbilities.Contains(guid))
                return true;

            // 이름 기반 폴백
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            return bpName.Contains("shapeflames") || bpName.Contains("fantheflames") ||
                   bpName.Contains("symphony") || bpName.Contains("교향곡");
        }

        /// <summary>
        /// 연쇄 효과 능력인지 확인 (Chain Lightning 등)
        /// </summary>
        public static bool IsChainEffectAbility(AbilityData ability)
        {
            if (ability == null) return false;

            string guid = AbilityDatabase.GetGuid(ability);
            if (!string.IsNullOrEmpty(guid) && ChainEffectAbilities.Contains(guid))
                return true;

            // 이름 기반 폴백
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            return bpName.Contains("chainlightning") || bpName.Contains("chain") ||
                   bpName.Contains("연쇄");
        }

        /// <summary>
        /// 콤보 권장 여부 확인
        /// 예: 적에게 Burning DoT가 없으면 먼저 Inferno 사용 권장
        /// </summary>
        public static string GetComboRecommendation(AbilityData ability, BaseUnitEntity target, ActionContext ctx)
        {
            if (ability == null || target == null) return null;

            var specialType = GetSpecialType(ability);

            if (specialType == SpecialAbilityType.DOTIntensify)
            {
                var dotType = InferDOTTypeFromAbility(ability);
                if (!HasDoT(target, dotType))
                {
                    // Burning DoT가 없으면 먼저 Inferno 사용 권장
                    if (dotType == DOTType.Burning || dotType == DOTType.PsykerBurning)
                    {
                        // 사용 가능한 Burning DoT 적용 스킬 찾기
                        foreach (var avail in ctx.AvailableAbilities)
                        {
                            if (AppliesBurningDOT(avail))
                            {
                                return $"Use {avail.Name} first to apply Burning DoT";
                            }
                        }
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
