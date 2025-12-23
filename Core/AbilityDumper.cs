using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Kingmaker;
using Kingmaker.Blueprints;
using Kingmaker.Blueprints.JsonSystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;

namespace CompanionAI_v2_2.Core
{
    /// <summary>
    /// v2.2.5: 능력 GUID 덤프 유틸리티
    /// 게임 내 모든 능력의 GUID와 속성을 파일로 출력
    /// </summary>
    public static class AbilityDumper
    {
        private static string DumpPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CompanionAI_AbilityDump.txt");

        private static string AllAbilitiesDumpPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "CompanionAI_AllAbilities.txt");

        /// <summary>
        /// 현재 파티원들의 모든 능력을 덤프
        /// </summary>
        public static void DumpPartyAbilities()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=".PadRight(100, '='));
                sb.AppendLine($"CompanionAI v2.2 - Party Abilities Dump");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("=".PadRight(100, '='));
                sb.AppendLine();

                var player = Game.Instance?.Player;
                if (player == null)
                {
                    sb.AppendLine("ERROR: No player instance found");
                    File.WriteAllText(DumpPath, sb.ToString());
                    Main.Log($"Ability dump failed - no player. File: {DumpPath}");
                    return;
                }

                var partyMembers = player.PartyAndPets;
                foreach (var unit in partyMembers)
                {
                    if (unit == null) continue;
                    DumpUnitAbilities(sb, unit);
                }

                File.WriteAllText(DumpPath, sb.ToString(), Encoding.UTF8);
                Main.Log($"Ability dump complete! File: {DumpPath}");
            }
            catch (Exception ex)
            {
                Main.LogError($"AbilityDumper error: {ex}");
            }
        }

        /// <summary>
        /// 게임 내 모든 BlueprintAbility 덤프 (ResourcesLibrary 사용)
        /// </summary>
        public static void DumpAllGameAbilities()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("=".PadRight(100, '='));
                sb.AppendLine($"CompanionAI v2.2 - ALL GAME ABILITIES DUMP");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("=".PadRight(100, '='));
                sb.AppendLine();

                // ResourcesLibrary에서 모든 BlueprintAbility 가져오기
                var blueprintsCache = ResourcesLibrary.BlueprintsCache;
                if (blueprintsCache == null)
                {
                    sb.AppendLine("ERROR: BlueprintsCache is null");
                    File.WriteAllText(AllAbilitiesDumpPath, sb.ToString());
                    Main.LogError("DumpAllGameAbilities: BlueprintsCache is null");
                    return;
                }

                // m_LoadedBlueprints 딕셔너리에 리플렉션으로 접근
                var cacheType = blueprintsCache.GetType();
                var loadedBlueprintsField = cacheType.GetField("m_LoadedBlueprints",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                if (loadedBlueprintsField == null)
                {
                    sb.AppendLine("ERROR: m_LoadedBlueprints field not found");
                    File.WriteAllText(AllAbilitiesDumpPath, sb.ToString());
                    Main.LogError("DumpAllGameAbilities: m_LoadedBlueprints field not found");
                    return;
                }

                var loadedBlueprints = loadedBlueprintsField.GetValue(blueprintsCache) as IDictionary;
                if (loadedBlueprints == null)
                {
                    sb.AppendLine("ERROR: m_LoadedBlueprints is null or not IDictionary");
                    File.WriteAllText(AllAbilitiesDumpPath, sb.ToString());
                    Main.LogError("DumpAllGameAbilities: m_LoadedBlueprints is null");
                    return;
                }

                var allAbilities = new List<BlueprintAbility>();
                int totalBlueprints = 0;
                int loadedCount = 0;

                foreach (DictionaryEntry kvp in loadedBlueprints)
                {
                    totalBlueprints++;
                    try
                    {
                        var entry = kvp.Value;
                        if (entry == null) continue;

                        // entry.Blueprint 가져오기 (리플렉션)
                        var entryType = entry.GetType();
                        var blueprintProp = entryType.GetProperty("Blueprint");
                        var blueprintField = entryType.GetField("Blueprint", BindingFlags.Public | BindingFlags.Instance);

                        object blueprintObj = null;
                        if (blueprintProp != null)
                            blueprintObj = blueprintProp.GetValue(entry);
                        else if (blueprintField != null)
                            blueprintObj = blueprintField.GetValue(entry);

                        if (blueprintObj is BlueprintAbility ability)
                        {
                            allAbilities.Add(ability);
                            loadedCount++;
                        }
                    }
                    catch { /* skip invalid entries */ }
                }

                sb.AppendLine($"Total Blueprint Entries: {totalBlueprints}");
                sb.AppendLine($"Already Loaded: {loadedCount}");
                sb.AppendLine($"BlueprintAbility Found: {allAbilities.Count}");
                sb.AppendLine();
                sb.AppendLine("NOTE: Only already-loaded blueprints are shown.");
                sb.AppendLine("Run this after playing for a while to get more abilities.");
                sb.AppendLine();

                // 카테고리별 분류
                var attacks = new List<BlueprintAbility>();
                var buffs = new List<BlueprintAbility>();
                var selfOnly = new List<BlueprintAbility>();
                var mixed = new List<BlueprintAbility>();
                var noTarget = new List<BlueprintAbility>();

                foreach (var bp in allAbilities.OrderBy(a => a.name))
                {
                    bool canEnemy = bp.CanTargetEnemies;
                    bool canFriend = bp.CanTargetFriends;
                    bool canSelf = bp.CanTargetSelf;

                    if (canEnemy && !canFriend)
                        attacks.Add(bp);
                    else if (canFriend && !canEnemy)
                        buffs.Add(bp);
                    else if (canSelf && !canEnemy && !canFriend)
                        selfOnly.Add(bp);
                    else if (canEnemy && canFriend)
                        mixed.Add(bp);
                    else
                        noTarget.Add(bp);
                }

                // 출력
                sb.AppendLine("=== ATTACKS (CanTargetEnemies=True, CanTargetFriends=False) ===");
                sb.AppendLine($"Count: {attacks.Count}");
                sb.AppendLine();
                foreach (var bp in attacks) DumpBlueprint(sb, bp);

                sb.AppendLine();
                sb.AppendLine("=== BUFFS/SUPPORT (CanTargetFriends=True, CanTargetEnemies=False) ===");
                sb.AppendLine($"Count: {buffs.Count}");
                sb.AppendLine();
                foreach (var bp in buffs) DumpBlueprint(sb, bp);

                sb.AppendLine();
                sb.AppendLine("=== SELF ONLY (CanTargetSelf=True only) ===");
                sb.AppendLine($"Count: {selfOnly.Count}");
                sb.AppendLine();
                foreach (var bp in selfOnly) DumpBlueprint(sb, bp);

                sb.AppendLine();
                sb.AppendLine("=== MIXED (Both Enemy and Friend) ===");
                sb.AppendLine($"Count: {mixed.Count}");
                sb.AppendLine();
                foreach (var bp in mixed) DumpBlueprint(sb, bp);

                sb.AppendLine();
                sb.AppendLine("=== NO TARGET / OTHER ===");
                sb.AppendLine($"Count: {noTarget.Count}");
                sb.AppendLine();
                foreach (var bp in noTarget) DumpBlueprint(sb, bp);

                File.WriteAllText(AllAbilitiesDumpPath, sb.ToString(), Encoding.UTF8);
                Main.Log($"All abilities dump complete! Total: {allAbilities.Count}, File: {AllAbilitiesDumpPath}");
            }
            catch (Exception ex)
            {
                Main.LogError($"DumpAllGameAbilities error: {ex}");
            }
        }

        private static void DumpBlueprint(StringBuilder sb, BlueprintAbility bp)
        {
            string name = bp.Name ?? bp.name ?? "Unknown";
            string bpName = bp.name ?? "null";
            string guid = bp.AssetGuid?.ToString() ?? "no-guid";

            sb.AppendLine($"  [{bpName}]");
            sb.AppendLine($"    Name: {name}");
            sb.AppendLine($"    GUID: {guid}");
            sb.AppendLine($"    Enemy={bp.CanTargetEnemies}, Friend={bp.CanTargetFriends}, Self={bp.CanTargetSelf}");
            sb.AppendLine($"    Range: {bp.Range}");
            sb.AppendLine();
        }

        private static void DumpUnitAbilities(StringBuilder sb, BaseUnitEntity unit)
        {
            sb.AppendLine($"### {unit.CharacterName} ###");
            sb.AppendLine("-".PadRight(80, '-'));

            var abilities = unit.Abilities?.RawFacts;
            if (abilities == null || abilities.Count == 0)
            {
                sb.AppendLine("  (No abilities)");
                sb.AppendLine();
                return;
            }

            // 카테고리별 분류
            var attacks = new List<AbilityData>();
            var buffs = new List<AbilityData>();
            var heals = new List<AbilityData>();
            var others = new List<AbilityData>();

            foreach (var abilityFact in abilities)
            {
                var abilityData = abilityFact?.Data;
                if (abilityData == null) continue;

                var bp = abilityData.Blueprint;
                if (bp == null) continue;

                if (bp.CanTargetEnemies && !bp.CanTargetFriends)
                    attacks.Add(abilityData);
                else if (bp.CanTargetFriends && !bp.CanTargetEnemies)
                    buffs.Add(abilityData);
                else if (IsHealAbility(abilityData))
                    heals.Add(abilityData);
                else
                    others.Add(abilityData);
            }

            sb.AppendLine();
            sb.AppendLine("  [ATTACKS - CanTargetEnemies=True, CanTargetFriends=False]");
            foreach (var a in attacks) DumpAbility(sb, a);

            sb.AppendLine();
            sb.AppendLine("  [BUFFS/SUPPORT - CanTargetFriends=True, CanTargetEnemies=False]");
            foreach (var a in buffs) DumpAbility(sb, a);

            sb.AppendLine();
            sb.AppendLine("  [HEALS]");
            foreach (var a in heals) DumpAbility(sb, a);

            sb.AppendLine();
            sb.AppendLine("  [OTHERS - Both or Neither]");
            foreach (var a in others) DumpAbility(sb, a);

            sb.AppendLine();
        }

        private static void DumpAbility(StringBuilder sb, AbilityData ability)
        {
            var bp = ability.Blueprint;
            string name = ability.Name ?? "Unknown";
            string bpName = bp?.name ?? "null";
            string guid = bp?.AssetGuid?.ToString() ?? "no-guid";
            bool canEnemy = bp?.CanTargetEnemies ?? false;
            bool canFriend = bp?.CanTargetFriends ?? false;
            bool canSelf = bp?.CanTargetSelf ?? false;
            string range = bp?.Range.ToString() ?? "?";
            bool hasWeapon = ability.Weapon != null;

            sb.AppendLine($"    Name: {name}");
            sb.AppendLine($"    BP Name: {bpName}");
            sb.AppendLine($"    GUID: {guid}");
            sb.AppendLine($"    CanTargetEnemies: {canEnemy}");
            sb.AppendLine($"    CanTargetFriends: {canFriend}");
            sb.AppendLine($"    CanTargetSelf: {canSelf}");
            sb.AppendLine($"    Range: {range}");
            sb.AppendLine($"    HasWeapon: {hasWeapon}");
            sb.AppendLine();
        }

        private static bool IsHealAbility(AbilityData ability)
        {
            string name = ability.Name?.ToLower() ?? "";
            string bpName = ability.Blueprint?.name?.ToLower() ?? "";
            return name.Contains("heal") || name.Contains("mend") ||
                   name.Contains("medikit") || name.Contains("치유") ||
                   bpName.Contains("heal") || bpName.Contains("medikit");
        }

        /// <summary>
        /// 특정 유닛의 현재 사용 가능한 능력 덤프 (전투 중 호출)
        /// </summary>
        public static void DumpAvailableAbilities(BaseUnitEntity unit, List<AbilityData> abilities)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== {unit.CharacterName} Available Abilities (Combat) ===");
                sb.AppendLine($"Time: {DateTime.Now:HH:mm:ss}");
                sb.AppendLine($"Total: {abilities.Count}");
                sb.AppendLine();

                foreach (var ability in abilities)
                {
                    var bp = ability.Blueprint;
                    sb.AppendLine($"  {ability.Name}");
                    sb.AppendLine($"    GUID: {bp?.AssetGuid}");
                    sb.AppendLine($"    Enemy={bp?.CanTargetEnemies}, Friend={bp?.CanTargetFriends}, Weapon={ability.Weapon != null}");
                }

                // Append to existing file
                File.AppendAllText(DumpPath, sb.ToString() + "\n\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Main.LogError($"DumpAvailableAbilities error: {ex}");
            }
        }
    }
}
