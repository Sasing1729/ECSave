using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ECSave.EditorTools
{
    public sealed class GeneratedCodeWindow : EditorWindow
    {
        private Vector2 _listScroll;
        private Vector2 _codeScroll;
        private List<string> _files;
        private int _selected = -1;
        private string _code;
        private string _status;

        [MenuItem("Window/ECSave/Generated Code")]
        public static void Open()
        {
            var w = GetWindow<GeneratedCodeWindow>();
            w.titleContent = new GUIContent("Generated Code");
            w.minSize = new Vector2(900, 500);
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
                Refresh();

            if (GUILayout.Button("Copy Path", EditorStyles.toolbarButton, GUILayout.Width(90)))
            {
                if (_selected >= 0 && _selected < _files.Count)
                    EditorGUIUtility.systemCopyBuffer = _files[_selected];
            }

            GUILayout.Label(_status ?? "", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            if (_files == null || _files.Count == 0)
            {
                EditorGUILayout.LabelField("(no generated files found)");
            }
            else
            {
                for (int i = 0; i < _files.Count; i++)
                {
                    var display = Path.GetFileName(_files[i]);
                    var style = i == _selected ? EditorStyles.boldLabel : EditorStyles.label;
                    if (GUILayout.Button(display, style))
                    {
                        _selected = i;
                        LoadCode(_files[i]);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            _codeScroll = EditorGUILayout.BeginScrollView(_codeScroll);

            if (string.IsNullOrEmpty(_code))
            {
                EditorGUILayout.LabelField("Select a file to view.");
            }
            else
            {
                var textStyle = new GUIStyle(EditorStyles.label) { wordWrap = false };
                EditorGUILayout.TextArea(_code, textStyle, GUILayout.ExpandHeight(true));
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void Refresh()
        {
            _files = new List<string>();
            _selected = -1;
            _code = null;
            _status = null;

            try
            {
                var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                {
                    _status = "Project root not found.";
                    return;
                }

                SearchGeneratedFiles(Path.Combine(projectRoot, "Library", "Bee", "artifacts"), _files);
                SearchGeneratedFiles(Path.Combine(projectRoot, "Temp", "obj"), _files);

                _files.Sort(StringComparer.OrdinalIgnoreCase);
                _status = _files.Count + " file(s)";
            }
            catch (Exception ex)
            {
                _status = "Error: " + ex.Message;
            }
        }

        private static void SearchGeneratedFiles(string root, List<string> output)
        {
            if (!Directory.Exists(root)) return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(
                    root, "*.g.cs", SearchOption.AllDirectories))
                {
                    if (file.IndexOf("ECSave.SourceGenerator", StringComparison.OrdinalIgnoreCase) >= 0)
                        output.Add(file);
                }
            }
            catch
            {
                // アクセス不可フォルダは無視
            }
        }

        private void LoadCode(string path)
        {
            try
            {
                _code = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                _code = "// Failed to read: " + ex.Message;
            }
        }
    }
}
