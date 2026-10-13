using UnityEditor;
using UnityEngine;
using ECSave.Format;

namespace ECSave.EditorTools
{
    internal sealed class LargeDataSummaryView
    {
        public void Draw(SaveDataInspection inspection)
        {
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("File", inspection.FilePath);
            EditorGUILayout.LabelField("Size", inspection.FileSize + " bytes");
            EditorGUILayout.LabelField("Entities", inspection.LogicalEntityCount.ToString());
            EditorGUILayout.LabelField("Sections", inspection.Sections.Length.ToString());

            int componentSections = 0;
            for (int i = 0; i < inspection.Sections.Length; i++)
            {
                var s = inspection.Sections[i];
                if (s.Kind == SectionKind.Component || s.Kind == SectionKind.Buffer)
                    componentSections++;
            }

            EditorGUILayout.LabelField("Component/Buffer Sections", componentSections.ToString());

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Select a section in the left panel to view its details.",
                MessageType.Info);

            EditorGUI.indentLevel--;
        }
    }
}
