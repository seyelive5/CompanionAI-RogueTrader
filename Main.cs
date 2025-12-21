using System;
using System.Reflection;
using HarmonyLib;
using UnityModManagerNet;
using CompanionAI_v2.Settings;
using CompanionAI_v2.UI;

namespace CompanionAI_v2
{
    /// <summary>
    /// Companion AI v2 - Complete AI Replacement
    /// This mod completely replaces the game's AI for player companions,
    /// giving full control over ability selection, targeting, and movement.
    /// </summary>
    public static class Main
    {
        public static bool Enabled { get; private set; }
        public static UnityModManager.ModEntry ModEntry { get; private set; }
        public static ModSettings Settings { get; private set; }
        public static Harmony HarmonyInstance { get; private set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            try
            {
                ModEntry = modEntry;

                // Load settings
                Settings = ModSettings.Load(modEntry);

                // Setup callbacks
                modEntry.OnToggle = OnToggle;
                modEntry.OnGUI = OnGUI;
                modEntry.OnSaveGUI = OnSaveGUI;

                // Apply Harmony patches IMMEDIATELY in Load() - like the working EnhancedCompanionAI
                HarmonyInstance = new Harmony(modEntry.Info.Id);
                HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
                Log("Harmony patches applied");

                Log("Companion AI v2 initialized successfully!");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to load mod: {ex}");
                return false;
            }
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            Enabled = value;
            Log($"Mod {(value ? "enabled" : "disabled")}");
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            MainUI.OnGUI();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            Settings.Save(modEntry);
        }

        #region Logging

        public static void Log(string message)
        {
            ModEntry?.Logger.Log($"[CompanionAI_v2] {message}");
        }

        public static void LogWarning(string message)
        {
            ModEntry?.Logger.Warning($"[CompanionAI_v2] {message}");
        }

        public static void LogError(string message)
        {
            ModEntry?.Logger.Error($"[CompanionAI_v2] {message}");
        }

        public static void LogDebug(string message)
        {
            if (Settings?.EnableDebugLogging == true)
            {
                ModEntry?.Logger.Log($"[CompanionAI_v2][DEBUG] {message}");
            }
        }

        #endregion
    }
}
