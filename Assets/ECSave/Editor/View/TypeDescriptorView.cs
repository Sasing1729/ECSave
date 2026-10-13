using UnityEditor;
using UnityEngine;

namespace ECSave.EditorTools
{
    internal sealed class TypeDescriptorView
    {
        private bool _showHex;
        private string _hexText;
        private Vector2 _hexScroll;

        public void Draw(SectionInfo s)
        {
            EditorGUILayout.LabelField("Type Descriptor", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            if (!s.TypeDescriptorAvailable)
            {
                EditorGUILayout.LabelField("(no type descriptor)");
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUILayout.LabelField("EntryCount", s.EntryCount.ToString());
            EditorGUILayout.LabelField("RawEntryCount", s.RawEntryCount.ToString());
            EditorGUILayout.LabelField("SchemaHashInputLength", s.SchemaHashInput.Length.ToString());

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Fields", EditorStyles.boldLabel);

            if (s.FieldNames == null || s.FieldNames.Length == 0)
            {
                EditorGUILayout.LabelField("(no fields)");
            }
            else
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < s.FieldNames.Length; i++)
                    EditorGUILayout.LabelField("[" + i + "]", s.FieldNames[i]);
                EditorGUI.indentLevel--;
            }

            if (s.SchemaHashInput.Length > 0)
            {
                EditorGUILayout.Space(6);

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button(_showHex ? "Hide SchemaHashInput Hex" : "Show SchemaHashInput Hex",
                        EditorStyles.miniButton, GUILayout.Width(220)))
                {
                    _showHex = !_showHex;
                    if (_showHex)
                        _hexText = ToHex(s.SchemaHashInput);
                }

                if (GUILayout.Button("Copy Hex", EditorStyles.miniButton, GUILayout.Width(90)))
                {
                    _hexText = ToHex(s.SchemaHashInput);
                    EditorGUIUtility.systemCopyBuffer = _hexText;
                    Debug.Log("[ECSave] SchemaHashInput hex copied to clipboard (" +
                              s.SchemaHashInput.Length + " bytes)");
                }

                EditorGUILayout.EndHorizontal();

                if (_showHex && !string.IsNullOrEmpty(_hexText))
                {
                    EditorGUILayout.Space(4);
                    _hexScroll = EditorGUILayout.BeginScrollView(_hexScroll,
                        GUILayout.Height(100));
                    EditorGUILayout.TextArea(_hexText, EditorStyles.textArea,
                        GUILayout.ExpandHeight(true));
                    EditorGUILayout.EndScrollView();
                }
            }

            EditorGUI.indentLevel--;
        }

        private static string ToHex(byte[] data)
        {
            if (data == null || data.Length == 0) return string.Empty;

            var sb = new System.Text.StringBuilder(data.Length * 3 + 16);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                {
                    if (i % 16 == 0) sb.Append('\n');
                    else sb.Append(' ');
                }
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
