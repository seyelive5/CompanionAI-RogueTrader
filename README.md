# Companion AI v2 - Complete Replacement

**Warhammer 40K: Rogue Trader - Smart Companion AI Mod**

A Unity Mod Manager mod that completely replaces companion AI behavior in combat with intelligent, role-based decision making.

## Features

### Role-Based AI System
- **Balanced**: Jack of all trades, adapts to any situation
- **Tank**: Frontline fighter, draws aggro, protects allies
- **DPS**: Damage dealer, focuses on killing enemies quickly
- **Support**: Buffs/debuffs, heals, avoids front line
- **Hybrid**: Flexible melee/ranged based on situation
- **Sniper**: Long-range specialist, maintains distance

### Smart Combat Decisions
- **Friendly Fire Prevention**: AoE skills automatically avoid allies
- **Veil Degradation Awareness**: Psykers avoid pushing Veil to dangerous levels (15+)
- **HP Cost Ability Safety**: Skills like Blood Oath blocked when HP is low (<40%)
- **Momentum System Integration**: Uses Heroic Acts at 175+, prioritizes momentum generation when desperate
- **Intelligent Positioning**: Support/Sniper stay back, Tank moves forward
- **Finisher Detection**: Prioritizes killing low HP enemies

### Per-Character Customization
- Enable/disable AI per character
- Choose role for each character
- Set range preference (Melee/Ranged/Any)

## Installation

1. Install [Unity Mod Manager (UMM)](https://www.nexusmods.com/site/mods/21) if not already installed
2. Download the latest release
3. Extract to:
   ```
   %LOCALAPPDATA%Low\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\
   ```
4. Enable the mod in UMM

## Usage

1. Open UMM menu in game (Ctrl+F10 by default)
2. Find "Companion AI v2" section
3. Enable AI for each character with the checkbox
4. Select Role and Range Preference for each character

## Version History

### v2.1.0 - Momentum & Advanced Safety Systems
- **Momentum System Integration**
  - Detects party Momentum (0-200 range)
  - Uses Heroic Acts when Momentum >= 175
  - Prioritizes Momentum-generating abilities (War Hymn, Inspire) when Momentum <= 50
  - Righteous Fury abilities (Revel in Slaughter, Holy Rage) used immediately when available

- **Veil Degradation Awareness** (Psyker Safety)
  - Tracks Warp Veil level (0-20)
  - Blocks Major Psychic powers when Veil >= 15 (Perils of the Warp risk)
  - Warns but allows Minor powers at any Veil level
  - Prevents abilities that would push Veil into danger zone

- **HP Cost Ability Safety**
  - Detects self-damage abilities (Blood Oath, Exsanguination, etc.)
  - Blocks HP-cost skills when HP <= 40%
  - Prevents accidental self-kill from aggressive skill use

- **Defensive Stance Logic**
  - Tank uses Defensive Stance/Brace when HP <= 60% or surrounded by 3+ enemies
  - Intelligent buff tracking prevents repeated stance activation

- **Force Attack Fallback**
  - When all safe attacks are blocked, attempts basic weapon attack
  - Maintains Veil safety even in fallback mode
  - Prevents "stuck in movement loop" situations

### v2.0.10 - Component-Based AoE Detection
- Automatic detection of dangerous abilities from game component analysis
- Added: Scatter, BladeDance, CustomRam, StepThrough detection
- Fixed: Idira's "Adept Ultimate Ability" no longer hits allies

### v2.0.9 and earlier
See [README.txt](README.txt) for full changelog

## Technical Details

### Project Structure
```
CompanionAI_v2/
├── Core/
│   ├── GameAPI.cs          # Game system integration (Momentum, Veil, etc.)
│   ├── ActionContext.cs    # Combat context for decision making
│   ├── ActionDecision.cs   # Decision result types
│   ├── AbilityRules.cs     # Ability classification rules
│   └── CombatHelpers.cs    # Combat calculation helpers
├── Strategies/
│   ├── IUnitStrategy.cs    # Strategy interface
│   ├── BalancedStrategy.cs # Default balanced behavior
│   ├── TankStrategy.cs     # Tank-specific logic
│   ├── DPSStrategy.cs      # DPS-specific logic
│   ├── SupportStrategy.cs  # Support-specific logic
│   ├── SniperStrategy.cs   # Sniper-specific logic
│   └── HybridStrategy.cs   # Hybrid behavior
├── Patches/
│   └── TurnControllerPatch.cs  # Harmony patch for turn control
├── Settings/
│   └── ModSettings.cs      # Per-character settings
├── UI/
│   └── SettingsUI.cs       # UMM settings interface
├── Main.cs                 # Mod entry point
└── Info.json               # UMM mod info
```

### Requirements
- Warhammer 40K: Rogue Trader
- Unity Mod Manager 0.23.0+
- .NET Framework 4.8.1

## Building from Source

1. Clone the repository
2. Ensure game references are set up in the .csproj file
3. Build with MSBuild:
   ```powershell
   msbuild CompanionAI_v2.csproj /p:Configuration=Release
   ```
4. Copy output from `bin/Release/net481/` to UMM mod folder

## License

This mod is provided as-is for personal use with Warhammer 40K: Rogue Trader.

---

**한국어 설명은 [README.txt](README.txt)를 참조하세요.**
