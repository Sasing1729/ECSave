using System.Collections.Generic;
using ECSave;
using ECSave.Internal;
using UnityEditor;
using UnityEngine;

namespace ECSave.EditorTools
{
    public sealed class MigrationBindingWindow : EditorWindow
    {
        private Vector2 _scroll;
        private List<(string ScopeName, MigrationEntry Entry)> _entries;
        private List<(string ScopeName, RenamedEntry Entry)> _renames;
        private string _errorMessage;

        [MenuItem("Window/ECSave/Migration Bindings")]
        public static void Open()
        {
            var w = GetWindow<MigrationBindingWindow>();
            w.titleContent = new GUIContent("Migration Bindings");
            w.minSize = new Vector2(700, 400);
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
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_errorMessage))
            {
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Registered Migrations (" + (_entries?.Count ?? 0) + ")", EditorStyles.boldLabel);

            if (_entries == null || _entries.Count == 0)
            {
                EditorGUILayout.LabelField("(none)");
            }
            else
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < _entries.Count; i++)
                {
                    var (scopeName, entry) = _entries[i];

                    EditorGUILayout.LabelField("Scope", scopeName ?? "(unknown)");
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("NewTypeNameHash", "0x" + entry.NewTypeNameHash.ToString("X16"));
                    if (!string.IsNullOrEmpty(entry.NewTypeName))
                        EditorGUILayout.LabelField("NewTypeName", entry.NewTypeName);
                    EditorGUILayout.LabelField("OldSchemaHash", "0x" + entry.OldSchemaHash.ToString("X16"));
                    EditorGUILayout.LabelField("NewSchemaHash", "0x" + entry.NewSchemaHash.ToString("X16"));
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(2);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("RenamedFrom (" + (_renames?.Count ?? 0) + ")", EditorStyles.boldLabel);

            if (_renames == null || _renames.Count == 0)
            {
                EditorGUILayout.LabelField("(none)");
            }
            else
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < _renames.Count; i++)
                {
                    var (scopeName, entry) = _renames[i];

                    EditorGUILayout.LabelField("Scope", scopeName ?? "(unknown)");
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("OldFullNameHash", "0x" + entry.OldFullNameHash.ToString("X16"));
                    if (!string.IsNullOrEmpty(entry.OldFullName))
                        EditorGUILayout.LabelField("OldFullName", entry.OldFullName);
                    EditorGUILayout.LabelField("NewFullNameHash", "0x" + entry.NewFullNameHash.ToString("X16"));
                    if (!string.IsNullOrEmpty(entry.NewFullName))
                        EditorGUILayout.LabelField("NewFullName", entry.NewFullName);
                    EditorGUI.indentLevel--;
                    EditorGUILayout.Space(2);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            _entries = new List<(string, MigrationEntry)>();
            _renames = new List<(string, RenamedEntry)>();
            _errorMessage = null;

            var scopes = Resources.FindObjectsOfTypeAll<SaveScope>();
            if (scopes == null || scopes.Length == 0)
            {
                _errorMessage = "No SaveScope instances found in the project.";
                return;
            }

            for (int i = 0; i < scopes.Length; i++)
            {
                var scope = scopes[i];
                if (scope == null) continue;

                string scopeName;
                try
                {
                    scopeName = scope.GetType().FullName;
                }
                catch
                {
                    scopeName = "(unknown)";
                }

                SaveDefinition def;
                try
                {
                    def = scope.GetSaveSystem().Definition;
                }
                catch (System.Exception ex)
                {
                    _errorMessage = "Failed to build definition for " + scopeName + ": " + ex.Message;
                    continue;
                }

                if (def == null) continue;
                var table = def.Migrations;
                if (table == null) continue;

                foreach (var entry in table.EnumerateEntries())
                    _entries.Add((scopeName, entry));

                foreach (var rename in table.EnumerateRenamed())
                    _renames.Add((scopeName, rename));
            }
        }
    }
}
