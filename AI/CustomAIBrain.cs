using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using Kingmaker.Pathfinding;
using Kingmaker.View;
using Kingmaker.Utility;
using UnityEngine;
using CompanionAI_v2.Settings;

namespace CompanionAI_v2.AI
{
    /// <summary>
    /// Custom AI decision result
    /// </summary>
    public enum AIDecision
    {
        None,           // No action taken
        UseAbility,     // Use an ability
        Move,           // Move to position
        EndTurn         // End turn (no valid actions)
    }

    /// <summary>
    /// Planned action for the AI to execute
    /// </summary>
    public class PlannedAction
    {
        public AIDecision Decision { get; set; }
        public AbilityData Ability { get; set; }
        public MechanicEntity Target { get; set; }
        public Vector3 MovePosition { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Combat state analysis
    /// </summary>
    public class CombatAnalysis
    {
        public BaseUnitEntity Unit { get; set; }
        public List<BaseUnitEntity> Enemies { get; set; } = new List<BaseUnitEntity>();
        public List<BaseUnitEntity> Allies { get; set; } = new List<BaseUnitEntity>();
        public float NearestEnemyDistance { get; set; }
        public BaseUnitEntity NearestEnemy { get; set; }
        public BaseUnitEntity WeakestEnemy { get; set; }
        public BaseUnitEntity MostDangerousEnemy { get; set; }
        public bool IsEngaged { get; set; }
        public int AvailableAP { get; set; }
        public float HPPercent { get; set; }
    }

    /// <summary>
    /// Custom AI Brain - Complete replacement for game's AI
    /// </summary>
    public static class CustomAIBrain
    {
        /// <summary>
        /// Execute a single action for the unit
        /// Called from PartUnitBrain.Tick() patch (called repeatedly per tick)
        /// </summary>
        public static void ExecuteTurn(BaseUnitEntity unit, CharacterSettings settings)
        {
            try
            {
                // Analyze combat situation
                var analysis = AnalyzeCombat(unit);

                // Check if we have AP
                if (analysis.AvailableAP <= 0)
                {
                    Main.Log($"[AI] {unit.CharacterName} - No AP remaining");
                    return;
                }

                // Decide next action
                var action = DecideNextAction(unit, settings, analysis);

                if (action.Decision == AIDecision.EndTurn || action.Decision == AIDecision.None)
                {
                    Main.Log($"[AI] {unit.CharacterName} - {action.Reason}");
                    return;
                }

                // Execute the action (just one!)
                bool success = ExecuteAction(unit, action);

                if (!success)
                {
                    Main.LogWarning($"[AI] {unit.CharacterName} - Action failed: {action.Reason}");
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[AI] Error executing turn for {unit.CharacterName}: {ex}");
            }
        }

        #region Combat Analysis

        /// <summary>
        /// Analyze the current combat situation
        /// </summary>
        private static CombatAnalysis AnalyzeCombat(BaseUnitEntity unit)
        {
            var analysis = new CombatAnalysis { Unit = unit };

            try
            {
                // Get all units
                var allUnits = Game.Instance.State.AllBaseAwakeUnits;

                // Separate enemies and allies
                foreach (var other in allUnits)
                {
                    if (other == null || other == unit)
                        continue;

                    if (other.LifeState.IsDead || other.LifeState.IsUnconscious)
                        continue;

                    if (!other.IsInCombat)
                        continue;

                    if (other.IsPlayerFaction == unit.IsPlayerFaction)
                    {
                        analysis.Allies.Add(other);
                    }
                    else
                    {
                        analysis.Enemies.Add(other);
                    }
                }

                // Find nearest enemy
                float nearestDist = float.MaxValue;
                foreach (var enemy in analysis.Enemies)
                {
                    float dist = Vector3.Distance(unit.Position, enemy.Position);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        analysis.NearestEnemy = enemy;
                    }
                }
                analysis.NearestEnemyDistance = nearestDist;

                // Find weakest enemy (lowest HP%)
                float lowestHP = float.MaxValue;
                foreach (var enemy in analysis.Enemies)
                {
                    float hpPercent = (float)enemy.Health.HitPointsLeft / enemy.Health.MaxHitPoints;
                    if (hpPercent < lowestHP)
                    {
                        lowestHP = hpPercent;
                        analysis.WeakestEnemy = enemy;
                    }
                }

                // Check if engaged in melee
                analysis.IsEngaged = unit.CombatState?.IsEngaged ?? false;

                // Calculate available AP
                analysis.AvailableAP = GetAvailableAP(unit);

                // Calculate HP percent
                analysis.HPPercent = (float)unit.Health.HitPointsLeft / unit.Health.MaxHitPoints * 100f;

                Main.LogDebug($"[AI] Analysis: {analysis.Enemies.Count} enemies, nearest at {analysis.NearestEnemyDistance:F1}m, engaged={analysis.IsEngaged}, AP={analysis.AvailableAP}, HP={analysis.HPPercent:F0}%");
            }
            catch (Exception ex)
            {
                Main.LogError($"[AI] Error analyzing combat: {ex}");
            }

            return analysis;
        }

        private static int GetAvailableAP(BaseUnitEntity unit)
        {
            var combatState = unit.CombatState;
            if (combatState == null)
                return 0;

            return (int)(combatState.ActionPointsBlue + combatState.ActionPointsYellow);
        }

        #endregion

        #region Decision Making

        /// <summary>
        /// Decide the next action to take
        /// </summary>
        private static PlannedAction DecideNextAction(BaseUnitEntity unit, CharacterSettings settings, CombatAnalysis analysis)
        {
            try
            {
                // Get all available abilities (bypass game's filtering!)
                var abilities = GetAllUsableAbilities(unit, analysis);

                Main.LogDebug($"[AI] {unit.CharacterName} has {abilities.Count} usable abilities");

                // No enemies = nothing to do
                if (analysis.Enemies.Count == 0)
                {
                    return new PlannedAction { Decision = AIDecision.EndTurn, Reason = "No enemies" };
                }

                // No abilities available
                if (abilities.Count == 0)
                {
                    return new PlannedAction { Decision = AIDecision.EndTurn, Reason = "No usable abilities" };
                }

                // Priority 1: Emergency healing if HP critically low
                if (analysis.HPPercent < 25)
                {
                    var healAbility = FindHealingAbility(abilities, unit);
                    if (healAbility != null)
                    {
                        return new PlannedAction
                        {
                            Decision = AIDecision.UseAbility,
                            Ability = healAbility,
                            Target = unit,
                            Reason = "Emergency self-heal"
                        };
                    }
                }

                // Priority 2: Apply buffs if we have enough AP
                if (settings.UseBuffsBeforeAttack && analysis.AvailableAP >= 4)
                {
                    var buffAbility = FindBestBuff(abilities, unit, analysis);
                    if (buffAbility != null)
                    {
                        return new PlannedAction
                        {
                            Decision = AIDecision.UseAbility,
                            Ability = buffAbility,
                            Target = unit,
                            Reason = "Applying buff before combat"
                        };
                    }
                }

                // Priority 3: For ranged characters - check if we need to move first
                if (settings.RangePreference == RangePreference.PreferRanged && analysis.IsEngaged)
                {
                    var retreatPos = FindRetreatPosition(unit, analysis, settings);
                    if (retreatPos != Vector3.zero)
                    {
                        return new PlannedAction
                        {
                            Decision = AIDecision.Move,
                            MovePosition = retreatPos,
                            Reason = "Retreating from melee for ranged attack"
                        };
                    }
                }

                // Priority 4: Attack!
                var attackResult = FindBestAttack(abilities, unit, settings, analysis);
                if (attackResult.Ability != null)
                {
                    return attackResult;
                }

                // Priority 5: Move closer to enemy if needed
                if (analysis.NearestEnemy != null)
                {
                    var approachPos = FindApproachPosition(unit, analysis.NearestEnemy, analysis);
                    if (approachPos != Vector3.zero)
                    {
                        return new PlannedAction
                        {
                            Decision = AIDecision.Move,
                            MovePosition = approachPos,
                            Reason = "Moving closer to attack"
                        };
                    }
                }

                return new PlannedAction { Decision = AIDecision.EndTurn, Reason = "No valid actions found" };
            }
            catch (Exception ex)
            {
                Main.LogError($"[AI] Error in decision making: {ex}");
                return new PlannedAction { Decision = AIDecision.EndTurn, Reason = "Error" };
            }
        }

        #endregion

        #region Ability Collection

        /// <summary>
        /// Get ALL usable abilities, bypassing game's restrictive filtering
        /// This is the key difference from the game's AI!
        /// </summary>
        private static List<AbilityData> GetAllUsableAbilities(BaseUnitEntity unit, CombatAnalysis analysis)
        {
            var result = new List<AbilityData>();

            try
            {
                var allAbilities = unit.Abilities.RawFacts;
                if (allAbilities == null)
                    return result;

                foreach (var ability in allAbilities)
                {
                    if (ability?.Data == null)
                        continue;

                    var data = ability.Data;

                    // Check AP cost
                    int apCost = data.CalculateActionPointCost();
                    if (apCost > analysis.AvailableAP)
                        continue;

                    // Check cooldown
                    if (data.IsOnCooldown)
                        continue;

                    // Check ammo/resources
                    if (data.Weapon != null && data.Weapon.CurrentAmmo <= 0)
                        continue;

                    // Skip abilities that truly can't be used
                    // BUT DON'T skip based on range or threatening area - we'll handle that ourselves!

                    result.Add(data);

                    Main.LogDebug($"[AI] Available: {data.Name} (AP: {apCost})");
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[AI] Error getting abilities: {ex}");
            }

            return result;
        }

        #endregion

        #region Ability Selection

        private static AbilityData FindHealingAbility(List<AbilityData> abilities, BaseUnitEntity unit)
        {
            // Look for self-heal abilities
            foreach (var ability in abilities)
            {
                if (ability.Blueprint == null)
                    continue;

                // Check if it's a healing ability targeting self
                if (ability.Blueprint.CanTargetSelf &&
                    (ability.Name.Contains("Heal") || ability.Name.Contains("치료") ||
                     ability.Name.Contains("Medikit") || ability.Name.Contains("메디킷")))
                {
                    return ability;
                }
            }
            return null;
        }

        private static AbilityData FindBestBuff(List<AbilityData> abilities, BaseUnitEntity unit, CombatAnalysis analysis)
        {
            // Look for self-buff abilities that aren't already active
            foreach (var ability in abilities)
            {
                if (ability.Blueprint == null)
                    continue;

                // Personal range usually means self-buff
                if (ability.Blueprint.Range == Kingmaker.UnitLogic.Abilities.Blueprints.AbilityRange.Personal)
                {
                    // Check if we already have this buff
                    bool hasBuff = false;
                    foreach (var buff in unit.Buffs.Enumerable)
                    {
                        if (buff.Name == ability.Name)
                        {
                            hasBuff = true;
                            break;
                        }
                    }

                    if (!hasBuff)
                    {
                        return ability;
                    }
                }
            }
            return null;
        }

        private static PlannedAction FindBestAttack(List<AbilityData> abilities, BaseUnitEntity unit,
            CharacterSettings settings, CombatAnalysis analysis)
        {
            AbilityData bestAbility = null;
            BaseUnitEntity bestTarget = null;
            float bestScore = float.MinValue;

            foreach (var ability in abilities)
            {
                if (ability.Blueprint == null)
                    continue;

                // Skip non-offensive abilities
                if (!ability.Blueprint.CanTargetEnemies)
                    continue;

                // Get weapon range
                int range = GetAbilityRange(ability);

                // Check each enemy as a target
                foreach (var enemy in analysis.Enemies)
                {
                    float distance = Vector3.Distance(unit.Position, enemy.Position);

                    // Check if in range
                    if (distance > range)
                        continue;

                    // Check minimum range (for ranged weapons)
                    int minRange = ability.MinRangeCells;
                    if (minRange > 0 && distance < minRange)
                        continue;

                    // Score this target
                    float score = ScoreTarget(ability, enemy, settings, analysis);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestAbility = ability;
                        bestTarget = enemy;
                    }
                }
            }

            if (bestAbility != null)
            {
                return new PlannedAction
                {
                    Decision = AIDecision.UseAbility,
                    Ability = bestAbility,
                    Target = bestTarget,
                    Reason = $"Attack {bestTarget.CharacterName} with {bestAbility.Name}"
                };
            }

            return new PlannedAction { Decision = AIDecision.None };
        }

        private static int GetAbilityRange(AbilityData ability)
        {
            if (ability.Weapon?.Blueprint != null)
            {
                return ability.Weapon.Blueprint.AttackRange;
            }
            return ability.Blueprint?.GetRange() ?? 0;
        }

        private static float ScoreTarget(AbilityData ability, BaseUnitEntity enemy,
            CharacterSettings settings, CombatAnalysis analysis)
        {
            float score = 100f;

            // Prefer low HP targets (finish them off)
            float enemyHPPercent = (float)enemy.Health.HitPointsLeft / enemy.Health.MaxHitPoints;
            if (settings.FinishLowHPEnemies && enemyHPPercent < 0.3f)
            {
                score += 50f; // Big bonus for nearly dead enemies
            }
            else
            {
                score -= enemyHPPercent * 20f; // Slight preference for wounded targets
            }

            // Prefer closer targets (less movement needed)
            float distance = Vector3.Distance(analysis.Unit.Position, enemy.Position);
            score -= distance * 2f;

            return score;
        }

        #endregion

        #region Movement

        private static Vector3 FindRetreatPosition(BaseUnitEntity unit, CombatAnalysis analysis, CharacterSettings settings)
        {
            // Simple retreat: move directly away from nearest enemy
            if (analysis.NearestEnemy == null)
                return Vector3.zero;

            Vector3 awayDirection = (unit.Position - analysis.NearestEnemy.Position).normalized;
            Vector3 retreatPos = unit.Position + awayDirection * settings.MinSafeDistance;

            // TODO: Validate this position is walkable and within AP range
            // For now, just return the direction

            return retreatPos;
        }

        private static Vector3 FindApproachPosition(BaseUnitEntity unit, BaseUnitEntity target, CombatAnalysis analysis)
        {
            // Move towards target
            Vector3 direction = (target.Position - unit.Position).normalized;
            Vector3 approachPos = unit.Position + direction * 3f; // Move 3m closer

            // TODO: Validate position

            return approachPos;
        }

        #endregion

        #region Action Execution

        /// <summary>
        /// Execute a planned action
        /// </summary>
        private static bool ExecuteAction(BaseUnitEntity unit, PlannedAction action)
        {
            try
            {
                Main.Log($"[AI] {unit.CharacterName} executing: {action.Decision} - {action.Reason}");

                switch (action.Decision)
                {
                    case AIDecision.UseAbility:
                        return ExecuteAbility(unit, action.Ability, action.Target);

                    case AIDecision.Move:
                        return ExecuteMove(unit, action.MovePosition);

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Main.LogError($"[AI] Error executing action: {ex}");
                return false;
            }
        }

        private static bool ExecuteAbility(BaseUnitEntity unit, AbilityData ability, MechanicEntity target)
        {
            try
            {
                // Create target wrapper
                TargetWrapper targetWrapper;
                if (target != null)
                {
                    targetWrapper = new TargetWrapper(target);
                }
                else
                {
                    targetWrapper = new TargetWrapper(unit);
                }

                // Queue the command - this is the same API the game uses internally
                var commandParams = new UnitUseAbilityParams(ability, targetWrapper);
                unit.Commands.Run(commandParams);

                Main.Log($"[AI] Executing ability: {ability.Name} -> {target?.Name ?? "self"}");
                return true;
            }
            catch (Exception ex)
            {
                Main.LogError($"[AI] Error executing ability: {ex}");
                return false;
            }
        }

        private static bool ExecuteMove(BaseUnitEntity unit, Vector3 position)
        {
            try
            {
                // TODO: Create and execute movement command
                // This requires calculating a valid path

                Main.Log($"[AI] Movement to {position} - NOT YET IMPLEMENTED");
                return false;
            }
            catch (Exception ex)
            {
                Main.LogError($"[AI] Error executing move: {ex}");
                return false;
            }
        }

        #endregion
    }
}
