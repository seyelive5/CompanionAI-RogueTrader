using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using UnityEngine;
using CompanionAI_v2.Settings;

namespace CompanionAI_v2.UI
{
    /// <summary>
    /// 메인 UI - 로컬라이징 지원 및 2배 크기 버튼
    /// </summary>
    public static class MainUI
    {
        private static string _selectedCharacterId = "";
        private static CharacterSettings _editingSettings = null;
        private static Vector2 _scrollPosition = Vector2.zero;

        // 스타일
        private static GUIStyle _headerStyle;
        private static GUIStyle _boldLabelStyle;
        private static GUIStyle _boxStyle;
        private static GUIStyle _descriptionStyle;

        // UI 크기 상수 (2배 크기)
        private const float CHECKBOX_SIZE = 50f;         // 25 -> 50
        private const float BUTTON_HEIGHT = 50f;         // 25 -> 50
        private const float ROLE_BUTTON_WIDTH = 120f;    // 85 -> 120
        private const float RANGE_BUTTON_WIDTH = 150f;   // 120 -> 150
        private const float CHAR_NAME_WIDTH = 180f;      // 150 -> 180
        private const float ROLE_LABEL_WIDTH = 120f;     // 100 -> 120
        private const float RANGE_LABEL_WIDTH = 160f;    // 140 -> 160
        private const float LANG_BUTTON_WIDTH = 100f;

        // Localization helper
        private static string L(string key) => Localization.Get(key);

        public static void OnGUI()
        {
            // Sync language setting
            Localization.CurrentLanguage = Main.Settings.UILanguage;

            InitStyles();

            try
            {
                _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(700));
                GUILayout.BeginVertical("box");

                DrawHeader();
                DrawDivider();

                DrawGlobalSettings();
                DrawDivider();

                DrawCharacterSelection();

                GUILayout.EndVertical();
                GUILayout.EndScrollView();
            }
            catch (Exception ex)
            {
                GUILayout.Label($"<color=#FF0000>UI Error: {ex.Message}</color>");
                Main.LogError($"[UI] Draw error: {ex}");
            }
        }

        private static void InitStyles()
        {
            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    richText = true
                };
            }

            if (_boldLabelStyle == null)
            {
                _boldLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                    richText = true
                };
            }

            if (_descriptionStyle == null)
            {
                _descriptionStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_boxStyle == null)
            {
                _boxStyle = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(15, 15, 15, 15)
                };
            }
        }

        private static void DrawDivider()
        {
            GUILayout.Space(15);
        }

        // 설명 텍스트 색상 (더 밝은 회색)
        private const string DESC_COLOR = "#D8D8D8";

        private static void DrawHeader()
        {
            GUILayout.Label($"<color=#00FFFF><b>{L("Title")}</b></color>", _headerStyle);
            GUILayout.Label($"<color={DESC_COLOR}>{L("Subtitle")}</color>", _descriptionStyle);
        }

        private static void DrawGlobalSettings()
        {
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label($"<b>{L("GlobalSettings")}</b>", _boldLabelStyle);
            GUILayout.Space(10);

            // Language Selection
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{L("Language")}:</b>", _boldLabelStyle, GUILayout.Width(100));
            GUILayout.Space(10);

            foreach (Language lang in Enum.GetValues(typeof(Language)))
            {
                bool isSelected = Main.Settings.UILanguage == lang;
                string langName = lang == Language.English ? "English" : "한국어";

                string buttonText = isSelected
                    ? $"<color=#00FF00><b>{langName}</b></color>"
                    : $"<color=#D8D8D8>{langName}</color>";

                if (GUILayout.Button(buttonText, GUI.skin.button, GUILayout.Width(LANG_BUTTON_WIDTH), GUILayout.Height(40)))
                {
                    Main.Settings.UILanguage = lang;
                    Localization.CurrentLanguage = lang;
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            // Debug Logging
            Main.Settings.EnableDebugLogging = DrawCheckbox(
                Main.Settings.EnableDebugLogging,
                L("EnableDebugLogging")
            );

            // AI Decision Log
            Main.Settings.ShowAIThoughts = DrawCheckbox(
                Main.Settings.ShowAIThoughts,
                L("ShowAIDecisionLog")
            );

            GUILayout.EndVertical();
        }

        /// <summary>
        /// ToyBox 스타일 체크박스 - 2배 크기
        /// </summary>
        private static bool DrawCheckbox(bool value, string label)
        {
            GUILayout.BeginHorizontal();

            // 폰트가 체크마크를 지원하는지 확인
            bool useGlyphs = GUI.skin.font != null && GUI.skin.font.HasCharacter('✔');

            string checkIcon;
            if (useGlyphs)
            {
                checkIcon = value ? "<size=20><color=#00FF00>✔</color></size>" : "<size=20><color=#808080>✖</color></size>";
            }
            else
            {
                checkIcon = value ? "<size=16><b><color=green>[X]</color></b></size>" : "<size=16><b>[ ]</b></size>";
            }

            // 체크 박스 부분 (2배 크기)
            if (GUILayout.Button(checkIcon, GUI.skin.box, GUILayout.Width(CHECKBOX_SIZE), GUILayout.Height(CHECKBOX_SIZE)))
            {
                value = !value;
            }

            GUILayout.Space(10);

            // 라벨 부분 (더 큰 폰트)
            if (GUILayout.Button($"<size=14>{label}</size>", GUI.skin.label, GUILayout.Height(CHECKBOX_SIZE)))
            {
                value = !value;
            }

            GUILayout.EndHorizontal();

            return value;
        }

        private static void DrawCharacterSelection()
        {
            GUILayout.BeginVertical(_boxStyle);
            GUILayout.Label($"<b>{L("PartyMembers")}</b>", _boldLabelStyle);
            GUILayout.Space(10);

            var characters = GetPartyMembers();

            if (characters.Count == 0)
            {
                GUILayout.Label($"<color=#D8D8D8><i>{L("NoCharacters")}</i></color>", _descriptionStyle);
                GUILayout.EndVertical();
                return;
            }

            // 헤더
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{L("AI")}</b>", GUILayout.Width(60));
            GUILayout.Label($"<b>{L("Character")}</b>", GUILayout.Width(CHAR_NAME_WIDTH));
            GUILayout.Label($"<b>{L("Role")}</b>", GUILayout.Width(ROLE_LABEL_WIDTH));
            GUILayout.Label($"<b>{L("Range")}</b>", GUILayout.Width(RANGE_LABEL_WIDTH));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);

            foreach (var character in characters)
            {
                DrawCharacterRow(character);
            }

            GUILayout.EndVertical();
        }

        private static void DrawCharacterRow(CharacterInfo character)
        {
            string characterName = character.Name ?? "Unknown";
            string characterId = character.Id;
            var settings = Main.Settings.GetOrCreateSettings(characterId, characterName);

            // 캐릭터 행
            GUILayout.BeginHorizontal("box");

            // AI 활성화 토글 (2배 크기)
            bool useGlyphs = GUI.skin.font != null && GUI.skin.font.HasCharacter('✔');
            string checkIcon;
            if (useGlyphs)
            {
                checkIcon = settings.EnableCustomAI ? "<size=18><color=#00FF00>✔</color></size>" : "<size=18><color=#808080>✖</color></size>";
            }
            else
            {
                checkIcon = settings.EnableCustomAI ? "<size=14><b><color=green>[X]</color></b></size>" : "<size=14><b>[ ]</b></size>";
            }

            if (GUILayout.Button(checkIcon, GUI.skin.box, GUILayout.Width(CHECKBOX_SIZE), GUILayout.Height(CHECKBOX_SIZE)))
            {
                settings.EnableCustomAI = !settings.EnableCustomAI;
                Main.Log($"[UI] AI {(settings.EnableCustomAI ? "enabled" : "disabled")} for {characterName}");
            }

            // 캐릭터 이름 버튼 (2배 높이)
            bool isSelected = _selectedCharacterId == characterId;
            string buttonText = isSelected
                ? $"<b>▼ {characterName}</b>"
                : $"▶ {characterName}";

            if (GUILayout.Button(buttonText, GUI.skin.button, GUILayout.Width(CHAR_NAME_WIDTH), GUILayout.Height(CHECKBOX_SIZE)))
            {
                if (isSelected)
                {
                    _selectedCharacterId = "";
                    _editingSettings = null;
                }
                else
                {
                    _selectedCharacterId = characterId;
                    _editingSettings = settings;
                }
            }

            // Role 표시 (색상 코딩)
            string roleColor = GetRoleColor(settings.Role);
            string roleText = $"<color={roleColor}><b>{Localization.GetRoleName(settings.Role)}</b></color>";
            GUILayout.Label(roleText, GUILayout.Width(ROLE_LABEL_WIDTH), GUILayout.Height(CHECKBOX_SIZE));

            // Range Preference 표시
            GUILayout.Label(Localization.GetRangeName(settings.RangePreference), GUILayout.Width(RANGE_LABEL_WIDTH), GUILayout.Height(CHECKBOX_SIZE));

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 상세 설정 - 선택된 캐릭터의 바로 밑에 표시 (아코디언 스타일)
            if (isSelected && _editingSettings != null)
            {
                GUILayout.BeginVertical("box");
                DrawCharacterAISettings();
                GUILayout.EndVertical();
            }
        }

        private static string GetRoleColor(AIRole role)
        {
            return role switch
            {
                AIRole.Tank => "#4169E1",      // Royal Blue
                AIRole.DPS => "#FF6347",       // Tomato
                AIRole.Support => "#FFD700",   // Gold
                AIRole.Hybrid => "#90EE90",    // Light Green (근접/원거리 겸용)
                AIRole.Sniper => "#00CED1",    // Dark Cyan
                AIRole.Balanced => "#DDA0DD",  // Plum
                _ => "#FFFFFF"
            };
        }

        private static void DrawCharacterAISettings()
        {
            if (_editingSettings == null) return;

            GUILayout.Space(10);

            // Role 선택
            DrawRoleSelection();
            GUILayout.Space(15);

            // Range Preference 선택
            DrawRangePreferenceSelection();
            GUILayout.Space(10);
        }

        private static void DrawRoleSelection()
        {
            GUILayout.Label($"<b>{L("CombatRole")}</b>", _boldLabelStyle);
            GUILayout.Label($"<color=#D8D8D8><i>{L("CombatRoleDesc")}</i></color>", _descriptionStyle);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();

            foreach (AIRole role in Enum.GetValues(typeof(AIRole)))
            {
                string roleColor = GetRoleColor(role);
                bool isSelected = _editingSettings.Role == role;
                string roleName = Localization.GetRoleName(role);

                string buttonText = isSelected
                    ? $"<color={roleColor}><b>{roleName}</b></color>"
                    : $"<color=#D8D8D8>{roleName}</color>";

                if (GUILayout.Toggle(isSelected, buttonText, GUI.skin.button, GUILayout.Width(ROLE_BUTTON_WIDTH), GUILayout.Height(BUTTON_HEIGHT)))
                {
                    _editingSettings.Role = role;
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // Role 설명
            string roleDesc = Localization.GetRoleDescription(_editingSettings.Role);
            GUILayout.Label($"<color=#D8D8D8><i>{roleDesc}</i></color>", _descriptionStyle);
        }

        private static void DrawRangePreferenceSelection()
        {
            GUILayout.Label($"<b>{L("RangePreference")}</b>", _boldLabelStyle);
            GUILayout.Label($"<color=#D8D8D8><i>{L("RangePreferenceDesc")}</i></color>", _descriptionStyle);
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();

            foreach (RangePreference pref in Enum.GetValues(typeof(RangePreference)))
            {
                bool isSelected = _editingSettings.RangePreference == pref;
                string prefName = Localization.GetRangeName(pref);

                string buttonText = isSelected
                    ? $"<b>{prefName}</b>"
                    : $"<color=#D8D8D8>{prefName}</color>";

                if (GUILayout.Toggle(isSelected, buttonText, GUI.skin.button, GUILayout.Width(RANGE_BUTTON_WIDTH), GUILayout.Height(BUTTON_HEIGHT)))
                {
                    _editingSettings.RangePreference = pref;
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // Range preference 설명
            string prefDesc = Localization.GetRangeDescription(_editingSettings.RangePreference);
            GUILayout.Label($"<color=#D8D8D8><i>{prefDesc}</i></color>", _descriptionStyle);
        }

        #region Helper Methods

        private static List<CharacterInfo> GetPartyMembers()
        {
            try
            {
                if (Game.Instance?.Player == null)
                {
                    return new List<CharacterInfo>();
                }

                var partyMembers = Game.Instance.Player.PartyAndPets;
                if (partyMembers == null || partyMembers.Count == 0)
                {
                    return new List<CharacterInfo>();
                }

                return partyMembers
                    .Where(unit => unit != null)
                    .Select(unit => new CharacterInfo
                    {
                        Id = unit.UniqueId ?? "unknown",
                        Name = unit.CharacterName ?? "Unnamed",
                        Unit = unit
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                Main.LogError($"[UI] Error getting party members: {ex}");
                return new List<CharacterInfo>();
            }
        }

        private class CharacterInfo
        {
            public string Id { get; set; } = "";
            public string Name { get; set; }
            public BaseUnitEntity Unit { get; set; }
        }

        #endregion
    }
}
