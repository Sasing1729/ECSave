using UnityEditor;
using UnityEngine;

namespace ECSave.EditorTools
{
    internal sealed class ChunkComponentSectionView
    {
        public void Draw(SectionInfo s)
        {
            EditorGUILayout.LabelField("Chunk Component Section", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField("GroupCount", s.GroupCount.ToString());
            EditorGUILayout.LabelField("Stride", s.Stride.ToString());

            if (s.GroupCount == 0)
            {
                EditorGUILayout.LabelField("(empty section)");
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Groups", EditorStyles.boldLabel);

            for (int g = 0; g < (int)s.GroupCount; g++)
            {
                uint start = s.MemberOffset[g];
                uint end = s.MemberOffset[g + 1];
                int memberCount = (int)(end - start);

                EditorGUILayout.LabelField(
                    "Group " + g,
                    memberCount + " members (record index " + g + ")");

                if (memberCount > 0 && s.MemberEntityIndex != null)
                {
                    EditorGUI.indentLevel++;
                    int show = Mathf.Min(memberCount, 8);
                    for (int i = 0; i < show; i++)
                    {
                        uint pid = s.MemberEntityIndex[(int)start + i];
                        EditorGUILayout.LabelField("  Member[" + i + "]", "pid=" + pid);
                    }
                    if (memberCount > show)
                        EditorGUILayout.LabelField("  ...", (memberCount - show) + " more");
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUI.indentLevel--;
        }
    }
}
