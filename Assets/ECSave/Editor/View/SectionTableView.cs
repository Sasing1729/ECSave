using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ECSave.Format;

namespace ECSave.EditorTools
{
    internal sealed class SectionTableView
    {
        private static readonly Color SelectedColor = new Color(0.35f, 0.65f, 1f);

        private readonly Dictionary<SectionKind, string> _kindLabels = new Dictionary<SectionKind, string>
        {
            { SectionKind.Identity, "ID" },
            { SectionKind.Component, "C" },
            { SectionKind.Buffer, "B" },
            { SectionKind.Shared, "S" },
            { SectionKind.ChunkComponent, "K" },
            { SectionKind.Unknown, "?" },
        };

        private GUIStyle _itemStyle;
        private GUIStyle _itemSelectedStyle;
        private GUIStyle _summaryStyle;
        private GUIStyle _summarySelectedStyle;

        private void EnsureStyles()
        {
            if (_itemStyle != null) return;

            _itemStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 22,
                padding = new RectOffset(10, 6, 2, 2),
            };

            _itemSelectedStyle = new GUIStyle(_itemStyle)
            {
                fontStyle = FontStyle.Bold,
            };

            _summaryStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fixedHeight = 28,
                fontSize = 12,
            };

            _summarySelectedStyle = new GUIStyle(_summaryStyle)
            {
                fontStyle = FontStyle.Bold,
            };
        }

        private static bool DrawButton(string label, bool selected, GUIStyle normal, GUIStyle selectedStyle)
        {
            var prev = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = SelectedColor;
            bool clicked = GUILayout.Button(label, selected ? selectedStyle : normal);
            GUI.backgroundColor = prev;
            return clicked;
        }

        public int Draw(SaveDataInspection inspection, int selectedIndex)
        {
            EnsureStyles();

            if (DrawButton("\u25A4  Summary  (File Overview)", selectedIndex < 0, _summaryStyle, _summarySelectedStyle))
                selectedIndex = -1;

            EditorGUILayout.Space(6);

            var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, new Color(0.5f, 0.5f, 0.5f, 0.5f));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Sections (" + inspection.Sections.Length + ")", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            for (int i = 0; i < inspection.Sections.Length; i++)
            {
                var s = inspection.Sections[i];
                var kindLabel = _kindLabels.TryGetValue(s.Kind, out var k) ? k : "?";
                var label = "[" + kindLabel + "] " + (string.IsNullOrEmpty(s.Name) ? "(unnamed)" : s.Name);

                if (DrawButton(label, i == selectedIndex, _itemStyle, _itemSelectedStyle))
                    selectedIndex = i;
            }

            return selectedIndex;
        }
    }
}
