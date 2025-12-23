using System;
using System.Collections.Generic;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.6: 능력 GUID 데이터베이스
    /// 스킬 이름 대신 GUID로 안정적인 능력 식별
    ///
    /// GUID는 게임 버전/언어와 관계없이 동일함
    /// </summary>
    public static class AbilityGuids
    {
        #region Run and Gun (런 앤 건) - PostFirstAction

        /// <summary>
        /// Run and Gun 계열 - 공격 후 사용하면 무기 공격 재활성화
        /// </summary>
        public static readonly HashSet<string> RunAndGun = new HashSet<string>
        {
            "22a25a3e418246ccbe95f2cc81c17473",  // CSMHeavyBolter_DoubleTap_ability - 런 앤 건
            "cfc7943b71f04a1c9be6465946fc9ee2",  // ShotOnTheRun_MobAbility - 런 앤 건
            "5e60764f84c94277ae6a78b63a1fd2aa",  // SoldierShotOnTheRunAbility - 런 앤 건 (병사)
        };

        #endregion

        #region Daring Breach (대담한 돌파) - PostFirstAction

        /// <summary>
        /// Daring Breach 계열 - AP/MP 전체 회복
        /// </summary>
        public static readonly HashSet<string> DaringBreach = new HashSet<string>
        {
            "51366be5481b4ca7b348d9ac69a79f46",  // DaringBreachAbility - 대담한 돌파
            "845a1ed417f2489489eab670b00b773a",  // FighterDesperateAbility - 대담한 돌파 (투사)
            "ed21642647a14ead9a09183cd5318d11",  // FighterUltimateAbility - 대담한 돌파 (궁극)
        };

        #endregion

        #region Defensive Stance (방어 태세) - PreCombatBuff

        /// <summary>
        /// 방어 태세 계열 - 턴 시작 시 우선 사용
        /// </summary>
        public static readonly HashSet<string> DefensiveStance = new HashSet<string>
        {
            "cd42292391e74ba7809d0600ddb43a8d",  // DefensiveStanceAbility - 방어 태세
            "dfda4e8761d44549b0e70b10a71947fc",  // DefensiveStanceRecoverAbility - 방어 태세 회복
            "39247f7f6f024676a693a7e04fcc631d",  // Vanguard_DefensiveStance_Ability - 선봉대 방어 태세
        };

        #endregion

        #region Reload (재장전)

        /// <summary>
        /// 재장전 스킬
        /// </summary>
        public static readonly HashSet<string> Reload = new HashSet<string>
        {
            "98f4a31b68e446ad9c63411c7b349146",  // ReloadAbility - 재장전
            "b1704fc05eeb406ba23158061e765cac",  // ReloadNoAoOAbility - 재장전 (기회공격 무시)
            "121068f8b70641458b24b3edc31f9132",  // PlasmaPistolUniqueCh4_ReloadAbility - 플라즈마 재장전
            "1e3a9caa44f04f7696ad5bd4ec4056a3",  // ReloadKelermorph_Ability - 켈러모프 재장전
            "1cedb5f0cf104f57a88f91168e4c0df8",  // ReloadAfterCombatAbility - 전투 후 재장전
        };

        /// <summary>
        /// 신속 재장전 (버프)
        /// </summary>
        public static readonly HashSet<string> RapidReload = new HashSet<string>
        {
            "9c115131f98f4273af06890aa9d558d2",  // MA_Munitorium_RapidReload - 신속 재장전 1
            "386999d9601d4f2badad1186728bd772",  // MA_Munitorium_RapidReload2 - 신속 재장전 2
            "fcaad00a14fd4e9ca23222ac7daeb930",  // MA_Munitorium_RapidReload3 - 신속 재장전 3
            "2657c6ecadd84e1eb47658e65a493d37",  // ExaggeratingVisor_Stratagem_Ability - 신속 재장전 전략
        };

        #endregion

        #region Taunt/Provoke (도발)

        /// <summary>
        /// 도발 계열 - 적 어그로 유인
        /// </summary>
        public static readonly HashSet<string> Taunt = new HashSet<string>
        {
            "742ab23861c544b38f26e17175d17183",  // TauntAbility - 도발
            "46e7a840c3d04703b154660efb45538b",  // Vanguard_Disposal_Ability - 도발
            "a8c7d8404d104d4dad2d460ec2b470ee",  // ServoskullPet_InsanitySignal_Ability - 도발 신호
            "13e41af1d54c458da81050336ce8e0fc",  // FighterTauntingScreamAbility - 조롱하는 외침
            "383d89aaa52f4c3f8e19a02659ce19e7",  // TauntHelmet_Ability - 선동가의 투구
        };

        #endregion

        #region Finisher (마무리)

        /// <summary>
        /// 마무리 스킬 - 적 HP 낮을 때 효과적
        /// </summary>
        public static readonly HashSet<string> Finisher = new HashSet<string>
        {
            "6a4c3b65dff840e0aab5966ffe8aa7ba",  // ExactionCastigatorsMasteryExecute_Ability - 사형 선고
            "5b8545bc7a90491d865410a585071efe",  // TacticianFinishTheJobAbility - 임무 완수
            "cf6c3356a9b44dd7badea16a625687be",  // TacticianFinishTheJobSecondHandAbility - 임무 완수 (보조)
            "ed10346264414140936abd17d6c5b445",  // Ctan_SpearOfDestruction_FinishAbility - 해체의 황혼
            "614fe492067d4b50b03695782af00f00",  // PowerMaulFinisher - 파워몰 마무리
        };

        #endregion

        #region Heroic Act (영웅적 행위) - Momentum 175+

        /// <summary>
        /// Heroic Act 계열 - Momentum 175+ 필요
        /// </summary>
        public static readonly HashSet<string> HeroicAct = new HashSet<string>
        {
            "635161f3087c4294bf39c5fefe3d01af",  // ChainLightningPush_Ability - 연쇄 번개 (영웅적 행위)
            "fda0e6fc865d4712a8dd48a63bce326e",  // GiftOfNurgleHeroicActAbility - 너글의 선물
            "234425ce980548588fc9bb0fbd08497b",  // DataBlessingHeroicActAbility
            "ac688f9b6e8443da8380431780785eb8",  // ExquisiteCalibrationsHeroicActAbility
            "1497623133f74bcabd797aecdab2bb05",  // AssassinImmediateDispatchAbilityHeroic
            "8dfbb8da5a3b4a5b83e45934661fdd82",  // CannibalHeroicActAbility
            "bba2c59af522402f8a6c2690256b1f8e",  // EverlastingMassacre_HeroicAct_Ability
            "1a0e3b0471da4d61be248f36eac5fdaa",  // FrenzyHeroicActAbility - 광란
            "7a05cb34622f47fb8e704cebbfab3df8",  // ImmolationShotHeroicActAbility
            "189ed32cd3c746078d63bd98c58ef05f",  // PainReprisal_HeroicAct_Ability
            "8f34b8e6b92e48e09d411e34de5e5462",  // QuickBusinessHeroicActAbility - 빠른 공격
            "49977f6a6b414182bb6af1f130073981",  // TzeentchHorrorHeroicActAbility
            "741837887a4f429193ba44ad3948a71a",  // WychOverdoseHeroicActAbility
            "f8629a743eaf414eb8bf79edee9b02d0",  // Biomancy_Weapon_SyphonLife_Heroic_Ability - 생명력 흡수
            "14c15a3b56d54b30addcc1df8f4a6420",  // Biomancy_Weapon_SyphonLifeFeudalSculptor_AbilityHeroic
            "1598fb26e644442098e72562537ae660",  // Divination_Weapon_Consign_AbilityHeroic - 사형 선고
            "84ddefd28f224d5fb3f5e176375c1f05",  // Pyromancy_Weapon_Inferno_Heroic_Ability - 인페르노
            "bdde427505b14cd68b20ab0d915d5fe3",  // Sanctic_Weapon_EmperorsWrath_Heroic_Ability - 황제의 분노
            "9edd0e95bfea4532b764920a7b7f67bf",  // TelekinesisWeaponBindAbilityHeroic - 속박
            "5b19d80b3d694f77b84c2b38a04efe8f",  // TelepathyWeaponVisionOfDeath_Heroic_Ability - 죽음의 환영
        };

        #endregion

        #region Desperate Measure (필사적 조치)

        /// <summary>
        /// 필사적 조치 계열
        /// </summary>
        public static readonly HashSet<string> DesperateMeasure = new HashSet<string>
        {
            "f4dfd57e9b204e1e8d7eb2bb61a9ac11",  // VeteranDesperateAbility - 영웅적인 희생
        };

        #endregion

        #region Charge (돌격)

        /// <summary>
        /// 돌격 계열 - 이동+공격
        /// </summary>
        public static readonly HashSet<string> Charge = new HashSet<string>
        {
            "c78506dd0e14f7c45a599990e4e65038",  // ChargeAbility - 돌격
            "40800d54d3d64c7cb2d746cc2cce9a1b",  // CSMMelee_Charge_ability - 돌격 (CSM)
            "67f785ba0562480697aa3735bdd9e0c2",  // CSMUralon_Charge_ability - 돌격 (우랄론)
            "4955b43454f6488f82892e166c76c995",  // FighterCharge - 돌격 (투사)
            "d9f20b396eb64a4293c9e3bd3270e0dc",  // BullCharge_PowerArmor_Ability - 대적할수 없는 맹공
        };

        #endregion

        #region Healing (치료)

        /// <summary>
        /// 메디킷/치료 스킬
        /// </summary>
        public static readonly HashSet<string> Healing = new HashSet<string>
        {
            "b6e3c9398ea94c75afdbf61633ce2f85",  // BattleMedicsBoots_Medikit_ability - 메디킷
            "dd2e9a6170b448d4b2ec5a7fe0321e65",  // BattleStimulator_Medikit_ability - 전투 자극제 메디킷
            "ededbc48a7f24738a0fdb708fc48bb4c",  // ChirurgeonMedikit_Ability - 군의관 메디킷
            "1359b77cf6714555895e2d3577f6f9b9",  // GladiatorsFirstAidKit_ability - 검투사의 치료 키트
            "48ac9afb9b6d4caf8d488ea85d3d60ac",  // GladiatorsHealingPotion_ability - 피부 패치
            "2e9a23383b574408b4acdf6b62f6ed9b",  // HumanRavor_Medikit_Ability - 레이버의 메디킷
            "2081944e0fd8481e84c30ec03cfdc04e",  // ImperialMedic_Heal_Ability - 적절한 치료
            "d722bfac662c40f9b2a47dc6ea70d00a",  // LargeMedikit_ability - 대형 메디킷
            "0a88bfaa16ff41ea847ea14f58b384da",  // HealFreshTraumaAbility - 외상 치료
        };

        #endregion

        #region Gap Closer (갭 클로저)

        /// <summary>
        /// 갭 클로저 - 원거리에서 적에게 접근하는 스킬
        /// </summary>
        public static readonly HashSet<string> GapCloser = new HashSet<string>
        {
            // 돌격 계열도 갭 클로저로 포함
            "c78506dd0e14f7c45a599990e4e65038",  // ChargeAbility
            "40800d54d3d64c7cb2d746cc2cce9a1b",  // CSMMelee_Charge_ability
            "4955b43454f6488f82892e166c76c995",  // FighterCharge
            "8fed5098066b48efa1e09a14f7b8f6c6",  // TreasureWorld_AmbullMother_Emerge - 급습
            "c3e407372e02483e87b350235fc409f0",  // TreasureWorld_AmbullMother_EmergeWithTeleport - 급습
        };

        #endregion

        #region Combined Sets - PostFirstAction

        /// <summary>
        /// PostFirstAction 능력 전체 (Run and Gun + Daring Breach)
        /// 첫 공격 후 사용해야 효과적인 스킬들
        /// </summary>
        public static bool IsPostFirstAction(string guid)
        {
            return RunAndGun.Contains(guid) || DaringBreach.Contains(guid);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// GUID가 특정 카테고리에 속하는지 확인
        /// </summary>
        public static bool IsInCategory(string guid, HashSet<string> category)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            return category.Contains(guid);
        }

        /// <summary>
        /// 능력의 GUID 추출 (안전하게)
        /// </summary>
        public static string GetGuid(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            try
            {
                var guid = ability?.Blueprint?.AssetGuid?.ToString();
                return guid;
            }
            catch (Exception ex)
            {
                Main.LogDebug($"[AbilityGuids] GetGuid EXCEPTION: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 능력이 Run and Gun 계열인지 확인
        /// </summary>
        public static bool IsRunAndGun(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && RunAndGun.Contains(guid);
        }

        /// <summary>
        /// 능력이 Daring Breach 계열인지 확인
        /// </summary>
        public static bool IsDaringBreach(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && DaringBreach.Contains(guid);
        }

        /// <summary>
        /// 능력이 PostFirstAction 스킬인지 확인 (GUID 기반)
        /// </summary>
        public static bool IsPostFirstActionAbility(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && IsPostFirstAction(guid);
        }

        /// <summary>
        /// 능력이 Defensive Stance 계열인지 확인
        /// </summary>
        public static bool IsDefensiveStance(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && DefensiveStance.Contains(guid);
        }

        /// <summary>
        /// 능력이 Reload 스킬인지 확인
        /// </summary>
        public static bool IsReload(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && Reload.Contains(guid);
        }

        /// <summary>
        /// 능력이 Taunt 스킬인지 확인
        /// </summary>
        public static bool IsTaunt(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && Taunt.Contains(guid);
        }

        /// <summary>
        /// 능력이 Finisher 스킬인지 확인
        /// </summary>
        public static bool IsFinisher(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && Finisher.Contains(guid);
        }

        /// <summary>
        /// 능력이 Heroic Act 스킬인지 확인
        /// </summary>
        public static bool IsHeroicAct(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && HeroicAct.Contains(guid);
        }

        /// <summary>
        /// 능력이 Healing 스킬인지 확인
        /// </summary>
        public static bool IsHealing(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && Healing.Contains(guid);
        }

        /// <summary>
        /// 능력이 Charge 스킬인지 확인
        /// </summary>
        public static bool IsCharge(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && Charge.Contains(guid);
        }

        /// <summary>
        /// 능력이 Gap Closer 스킬인지 확인
        /// </summary>
        public static bool IsGapCloser(Kingmaker.UnitLogic.Abilities.AbilityData ability)
        {
            string guid = GetGuid(ability);
            return !string.IsNullOrEmpty(guid) && GapCloser.Contains(guid);
        }

        #endregion
    }
}
