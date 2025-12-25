# Companion AI v2.2

A companion AI mod for Warhammer 40,000: Rogue Trader that provides role-based automatic combat decisions for party members.

## What This Mod Does

This mod replaces the default companion AI behavior with a strategy-based system. When enabled, companions will automatically:

- Use abilities based on their assigned role (Tank, DPS, Support, Balanced)
- Consider ability timing (buffs before attacks, finishers on low HP enemies, etc.)
- Check for ally-safe AoE usage
- Manage reload and HP-cost abilities

## What This Mod Does NOT Do

- Does not control the player character
- Does not control enemy units
- Does not control familiars/pets (they use game's default AI)
- Does not guarantee optimal play in all situations
- Does not work with Turn-Based combat disabled

## Strategies

| Strategy | Behavior |
|----------|----------|
| Tank | Prioritizes defensive abilities, taunts, moves to engage enemies |
| DPS | Prioritizes damage output, uses finishers on low HP targets |
| Support | Prioritizes healing and buffs, maintains safe distance |
| Balanced | Adapts based on current situation |

## Requirements

- Unity Mod Manager (UMM) 0.23.0+
- Warhammer 40,000: Rogue Trader

## Installation

1. Install Unity Mod Manager
2. Download the mod files
3. Copy to: `%LOCALAPPDATA%Low\Owlcat Games\Warhammer 40000 Rogue Trader\UnityModManager\CompanionAI_v2.2\`
4. Required files: `CompanionAI_v2.2.dll`, `Info.json`

## Configuration

1. Open UMM menu in-game (Ctrl+F10)
2. Select CompanionAI v2.2 tab
3. For each character:
   - Check "Enable Custom AI"
   - Select a strategy

## Known Limitations

- Some ability combinations may not work as expected
- GUID database covers common abilities; unlisted abilities use fallback detection
- May conflict with other AI-modifying mods

## Compatibility

- Tested with game version 1.2.x
- Compatible with ToyBox (different functionality, no conflicts expected)
- May not be compatible with other AI replacement mods

## Technical Notes

- Uses Harmony patches on `TaskNodeSelectAbilityTarget` and `TaskNodeCastAbility`
- GUID-based ability identification (language-independent)
- Strategy pattern architecture for extensibility

## Version History

### v2.2.33
- Unified AbilityDatabase (merged AbilityRules + AbilityGuids)
- Added AbilityUsageTracker for centralized tracking
- Added SpecialAbilityHandler for DoT/chain effects

### v2.2.23
- Fixed ranged companions rushing to melee (ToClosestEnemy block)

### v2.2.14
- Fixed ability execution guarantee via TaskNodeCastAbility patch

## Source Code

https://github.com/seyelive5/CompanionAI-RogueTrader

## License

MIT License

## Credits

- Owlcat Games - Warhammer 40,000: Rogue Trader
- Unity Mod Manager Team
