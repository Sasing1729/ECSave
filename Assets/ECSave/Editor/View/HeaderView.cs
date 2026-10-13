using UnityEditor;
using UnityEngine;

namespace ECSave.EditorTools
{
    internal sealed class HeaderView
    {
        public void Draw(SaveDataInspection inspection)
        {
            EditorGUILayout.LabelField("Header", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("Magic", "0x" + inspection.Magic.ToString("X8"));
            EditorGUILayout.LabelField("FormatVersion", inspection.FormatVersion.ToString());
            EditorGUILayout.LabelField("HeaderFlags", "0x" + inspection.HeaderFlags.ToString("X4"));
            EditorGUILayout.LabelField("HeaderSize", inspection.HeaderSize.ToString());
            EditorGUILayout.LabelField("GameDataVersion", inspection.GameDataVersion.ToString());
            EditorGUILayout.LabelField("LogicalEntityCount", inspection.LogicalEntityCount.ToString());
            EditorGUILayout.LabelField("SectionCount", inspection.SectionCount.ToString());
            EditorGUILayout.LabelField("SectionTableOffset", "0x" + inspection.SectionTableOffset.ToString("X"));
            EditorGUILayout.LabelField("TotalLength", inspection.TotalLength.ToString() + " bytes");
            EditorGUILayout.LabelField("NameHeapLength", inspection.NameHeapLength.ToString());
            EditorGUILayout.LabelField("WorldIdLength", inspection.WorldIdLength.ToString());
            EditorGUILayout.LabelField("WorldId", inspection.WorldId ?? "");

            EditorGUI.indentLevel--;
        }
    }
}
