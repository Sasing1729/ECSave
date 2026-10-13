using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ECSave.Format;

namespace ECSave.EditorTools
{
    internal sealed class SectionTableView
    {
        private readonly Dictionary<SectionKind, string> _kindLabels = new Dictionary<SectionKind, string>
        {
            { SectionKind.Identity, "ID" },
            { SectionKind.Component, "C" },
            { SectionKind.Buffer, "B" },
            { SectionKind.Shared, "S" },
            { SectionKind.ChunkComponent, "K" },
            { SectionKind.Unknown, "?" },
        };

        public int Draw(SaveDataInspection inspection, int selectedIndex)
        {
            EditorGUILayout.LabelField("Sections (" + inspection.Sections.Length + ")", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            for (int i = 0; i < inspection.Sections.Length; i++)
            {
                var s = inspection.Sections[i];
                var kindLabel = _kindLabels.TryGetValue(s.Kind, out var k) ? k : "?";
                var label = "[" + kindLabel + "] " + (string.IsNullOrEmpty(s.Name) ? "(unnamed)" : s.Name);

                var style = i == selectedIndex ? EditorStyles.boldLabel : EditorStyles.label;

                if (GUILayout.Button(label, style))
                    selectedIndex = i;
            }

            return selectedIndex;
        }
    }
}
