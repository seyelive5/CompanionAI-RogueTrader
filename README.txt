================================================================================
                    Companion AI v2 - Complete Replacement
                         Warhammer 40K: Rogue Trader
================================================================================

[English]
---------
This mod completely replaces the AI behavior of your companions in combat.

FEATURES:
- Role-based AI (Tank, DPS, Support, Sniper, Hybrid, Balanced)
- Smart ability selection based on situation
- Friendly fire prevention (AoE skills avoid allies)
- Intelligent positioning (Support/Sniper stay back, Tank moves forward)
- Per-character customization

INSTALLATION:
1. Install Unity Mod Manager (UMM) if not already installed
2. Extract this folder to:
   %LOCALAPPDATA%Low\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\
3. Enable the mod in UMM

USAGE:
1. Open UMM menu in game (Ctrl+F10 by default)
2. Find "Companion AI v2" section
3. Enable AI for each character with the checkbox
4. Select Role and Range Preference for each character

ROLES:
- Balanced: Jack of all trades, adapts to situation
- Tank: Frontline fighter, draws aggro, protects allies
- DPS: Damage dealer, focuses on killing enemies quickly
- Support: Buffs/debuffs, heals, avoids front line
- Hybrid: Flexible melee/ranged based on situation
- Sniper: Long-range specialist, maintains distance

================================================================================

[Korean / 한국어]
-----------------
이 모드는 전투 중 동료의 AI 행동을 완전히 대체합니다.

기능:
- 역할 기반 AI (탱커, 딜러, 지원, 저격수, 하이브리드, 균형)
- 상황에 따른 스마트한 스킬 선택
- 아군 오사 방지 (광역 스킬이 아군을 피함)
- 지능적인 위치 선정 (지원/저격수는 후방, 탱커는 전방)
- 캐릭터별 개별 설정

설치 방법:
1. Unity Mod Manager (UMM)가 없다면 먼저 설치
2. 이 폴더를 다음 경로에 압축 해제:
   %LOCALAPPDATA%Low\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\
3. UMM에서 모드 활성화

사용 방법:
1. 게임 내에서 UMM 메뉴 열기 (기본: Ctrl+F10)
2. "Companion AI v2" 섹션 찾기
3. 각 캐릭터의 체크박스로 AI 활성화
4. 각 캐릭터의 역할과 거리 선호도 선택

역할 설명:
- 균형: 만능형, 상황에 맞는 최선의 행동
- 탱커: 최전방 전사, 어그로 유지, 아군 보호
- 딜러: 적을 빠르게 처치, 체력 낮은 적 우선
- 지원: 버프/디버프 우선, 아군 치유, 최전방 회피
- 하이브리드: 상황에 따라 근접/원거리 전환
- 저격수: 원거리 전문가, 거리 유지

================================================================================
                              Version 2.1.0
================================================================================

CHANGELOG:

v2.1.0 - MOMENTUM & ADVANCED SAFETY SYSTEMS:
- NEW: Momentum System Integration
  - Detects party Momentum (0-200 range, starts at 100)
  - Uses Heroic Acts when Momentum >= 175
  - Prioritizes Momentum-generating abilities when Momentum <= 50
  - Righteous Fury abilities (Revel in Slaughter, Holy Rage) used immediately

- NEW: Veil Degradation Awareness (Psyker Safety)
  - Tracks Warp Veil level (0-20)
  - Blocks Major Psychic powers when Veil >= 15
  - Prevents abilities that would push Veil into danger zone

- NEW: HP Cost Ability Safety
  - Blocks self-damage skills (Blood Oath, etc.) when HP <= 40%
  - Prevents accidental self-kill

- NEW: Defensive Stance Logic
  - Tank uses Defensive Stance when HP <= 60% or surrounded by 3+ enemies

- FIXED: "Stuck movement loop" - Force basic attack when all safe options blocked

v2.0.10 - COMPONENT-BASED DANGEROUS AoE DETECTION:
- MAJOR: Automatic detection of dangerous abilities from decompiled source analysis
  - Analyzed game source code to identify all ability components that can hit allies
  - IsDangerousAoE now auto-detects: Scatter, BladeDance, CustomRam, StepThrough
  - IsAoEAbility now detects: MeleeBurst, DirectMovement, DeliverChain, TargetsAround
  - No more manual addition of ability names needed for standard patterns

- FIXED: Idira's "실체 공격" (Adept Ultimate Ability) no longer hits allies
  - Added to DangerousAoE list manually (uses non-standard component)
  - Also added "ultimate" keyword detection for auto-blocking

- Technical details (from decompiled source):
  - ScatterPattern: hardcoded TargetType.Any
  - AbilityCustomRam: hardcoded TargetType.Any
  - AbilityStepThroughTarget: hardcoded TargetType.Any
  - AbilityCustomBladeDance: no faction check in targeting

- Previous v2.0.9 fixes included:
  - Tank buff penalties reduced (-80 to -40 for pre-engagement)
  - Taunt abilities now correctly target self for AoE effect
  - DPS+PreferMelee now properly uses ClosestEnemy movement mode
  - Removed aggressive PreferMelee blocking that prevented melee/buff usage

v2.0.8 - MELEE DPS POSITIONING FIX:
- FIXED: Melee DPS characters now properly engage in melee combat
  - Characters with PreferMelee setting now move to distance 1 (melee range)
  - Previously all DPS used idealDistance=6, ignoring PreferMelee setting
- FindBestPosition now respects RangePreference for positioning
- Kibellah and other melee characters will now close to melee instead of
  staying at range and only using bombs/debuffs

v2.0.7 - WIKI-VERIFIED ABILITY UPDATE:
- Extracted ALL 251 abilities from Fextralife wiki for verification
- Added 14 missing attack skills to explicit categorization:
  - Soldier/Ranged: Controlled Shot, Perfect Shot, Precise Attack
  - Bounty Hunter: Hunt Down the Prey, Ensnare the Prey
  - Pyromancy: Wildfire, Orchestrate Flames
  - Officer/Command: Raid
  - Psyker/Tormentor: Visions of Hell, Gift of Torment, Warp Curse Unleashed
  - Special: Dangerous Neighbourhood, Sword of Faith

- Added 4 new Dangerous AoE skills to AbilityRules:
  - Wildfire (fire spread AoE)
  - Dangerous Neighbourhood (area attack)
  - Visions of Hell (mental AoE)
  - Warp Curse Unleashed (curse AoE)

v2.0.6 - COMPREHENSIVE ABILITY REVIEW:
- MAJOR: Complete review of ALL 100+ abilities from game wiki
- MAJOR: Explicit categorization for 60+ attack skills to prevent misclassification

Attack Skills Now Properly Categorized:
  - Bladedancer/Reaper: Blade Dance, Death Waltz, Danse Macabre, Death From Above,
    Death Whisper, Killing Edge, Captive Audience
  - Assassin/Executioner: Dispatch, Death Blow, Terrifying Strike
  - Soldier/Warrior: Fighter Charge, Break Through, Assault Onslaught,
    Forceful Strike, Reckless Strike
  - Psyker: Psychic Scream/Shriek, Psychic Assault, Purge Soul, Evil Eye,
    Dominate, Mind Rupture, Sensory Deprivation, Waking Nightmare, Held in Gaze
  - Pyromancy: Ignite, Incinerate, Inflame, Molten Beam, Immolate the Soul
  - Soldier: Double Slug, Second Shot, Rapid Fire, Piercing Shot, Finish the Job
  - Bounty Hunter: Claim the Bounty, Wild Hunt
  - Officer: Orchestrated Firestorm, Last Volley
  - Familiar: Apprehend, Bite, Blinding Strike, Strafe, Obstruct Vision
  - Tormentor: Pain Resonance, Where It Hurts, Merciless Verdict
  - Others: Kick, Feinting Attack, Devastating Attack, Scourge of the Red Tide

Dangerous AoE Skills Added:
  - Molten Beam, Immolate the Soul (line attacks)
  - Psychic Assault (cone attack)
  - Scourge of the Red Tide, Zone of Fear, Spot of Apathy (area effects)

Self-Damage Skills Added:
  - Exsanguination, At All Costs, Carnival of Misery, Heroic Sacrifice

Finisher Skills Added:
  - Killing Edge, Death Blow, Finish the Job (bonus vs low HP enemies)

Keyword Conflict Fixes:
  - "reckless" no longer triggers Buff (Reckless Strike is Attack)
  - "charge" no longer triggers Movement (Fighter Charge is Attack)
  - Movement category now only includes pure movement (Dash, Teleport, etc.)

v2.0.5:
- CRITICAL FIX: Blade Dance (칼날 춤) no longer hits allies
  - Blade Dance attacks random nearby targets including allies (game mechanic)
  - AI now blocks this ability when allies are within melee range (1 cell)
- CRITICAL FIX: Added RandomMeleeAoE ability type for faction-ignoring attacks
- Fixed: Blade Dance now correctly categorized as Attack (was incorrectly Buff)
- Fixed: Death Waltz, Danse Macabre, Death From Above explicitly categorized as Attack

v2.0.4:
- Fixed: Psychic Scream now correctly categorized as Attack (was incorrectly Taunt)
- Fixed: Added Taunt abilities to offensive skill list (enemies only)
- Fixed: Added minimum score threshold - actions with score below 0 are rejected
- Improved: Taunt keyword matching more specific to avoid false positives

v2.0.3:
- Improved: Now uses game's actual hit chance calculation (RuleCalculateHitChances)
- Changed: Hit chance threshold raised from 30% to 60% for repositioning
- Improved: AI more aggressively repositions when hit chance is below 60%
- Fixed: Hit chance display now matches game UI exactly

v2.0.2:
- Added: Hit chance evaluation for ranged attacks
- Added: AI now moves to better position when hit chance is too low (<30%)
- Added: Cover and distance penalties affect attack decisions
- Improved: Ranged characters prioritize repositioning over low-accuracy shots

v2.0.1:
- Fixed: Attack skills no longer target allies when enemies are out of range
- Fixed: Offensive abilities (Attack/Debuff) now correctly only target enemies

v2.0.0:
- Initial release
- Complete AI replacement system
- Role-based behavior (Tank, DPS, Support, Sniper, Hybrid, Balanced)
- Friendly fire prevention for dangerous AoE abilities
- Localization support (English/Korean)

================================================================================
