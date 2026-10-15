using System;
using System.Collections.Generic;
using System.Reflection;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.Contract;
using UnityEditor;
using UnityEngine;

namespace ECSave.EditorTools
{
    public sealed class MigrationBindingWindow : EditorWindow
    {
        private const string PrefIncludeAll = "ECSave.MigrationBindings.IncludeAll";
        private const string PrefShowEmpty = "ECSave.MigrationBindings.ShowEmpty";
        private const string BindingsNamespace = "ECSave.Generated.Bindings";
        private const string RegistrationSuffix = "_ECSaveRegistration";
        private const string TempScopeName = "ECSave.MigrationBindings.Temp";

        private static bool s_bindingsEnsured;

        private enum RowStatus
        {
            Ok,
            Stale,
            Orphan,
            Unknown,
        }

        private sealed class MigrationRow
        {
            public MigrationEntry Entry;
            public string TypeName;
            public RowStatus Status;
            public ulong CurrentSchemaHash;
        }

        private sealed class ScopeView
        {
            public string Name;
            public bool IsAuto;
            public string Error;
            public int ModuleCount;
            public bool Bound;
            public List<MigrationRow> Rows = new List<MigrationRow>();
            public List<RenamedEntry> Renames = new List<RenamedEntry>();
        }

        private Vector2 _scroll;
        private List<(string ScopeName, MigrationEntry Entry)> _entries
            = new List<(string, MigrationEntry)>();
        private List<(string ScopeName, RenamedEntry Entry)> _renames
            = new List<(string, RenamedEntry)>();
        private readonly List<ScopeView> _scopes = new List<ScopeView>();
        private readonly List<SaveScope> _tempScopes = new List<SaveScope>();
        private readonly Dictionary<string, bool> _foldouts = new Dictionary<string, bool>();
        private string _errorMessage;
        private string _search = "";
        private bool _includeAllTypes = true;
        private bool _showEmptyScopes;

        [MenuItem("Window/ECSave/Migration Bindings")]
        public static void Open()
        {
            var w = GetWindow<MigrationBindingWindow>();
            w.titleContent = new GUIContent("Migration Bindings");
            w.minSize = new Vector2(760, 420);
        }

        private void OnEnable()
        {
            _includeAllTypes = EditorPrefs.GetBool(PrefIncludeAll, true);
            _showEmptyScopes = EditorPrefs.GetBool(PrefShowEmpty, false);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Refresh();
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            DestroyTempScopes();
        }

        private void OnFocus()
        {
            Refresh();
            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode
                || state == PlayModeStateChange.EnteredEditMode)
            {
                Refresh();
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!string.IsNullOrEmpty(_errorMessage))
            {
                EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                return;
            }

            int totalMigrations = 0;
            for (int i = 0; i < _scopes.Count; i++)
                totalMigrations += _scopes[i].Rows.Count;

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            int shown = 0;
            string filter = (_search ?? "").Trim();
            for (int i = 0; i < _scopes.Count; i++)
            {
                if (DrawScope(_scopes[i], filter)) shown++;
            }

            if (shown == 0)
            {
                EditorGUILayout.Space(8);
                if (!string.IsNullOrEmpty(filter))
                {
                    EditorGUILayout.HelpBox("No results for \"" + filter + "\".", MessageType.Info);
                }
                else if (totalMigrations == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No migrations are registered in any scope.\n" +
                        "Register one in SaveScope.Configure with b.RegisterMigration<T>().",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("Nothing to show.", MessageType.Info);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                Refresh();

            EditorGUI.BeginChangeCheck();
            _includeAllTypes = GUILayout.Toggle(
                _includeAllTypes, "All Scope Types", EditorStyles.toolbarButton, GUILayout.Width(110));
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(PrefIncludeAll, _includeAllTypes);
                Refresh();
            }

            EditorGUI.BeginChangeCheck();
            _showEmptyScopes = GUILayout.Toggle(
                _showEmptyScopes, "Show Empty", EditorStyles.toolbarButton, GUILayout.Width(90));
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(PrefShowEmpty, _showEmptyScopes);

            if (GUILayout.Button("Expand", EditorStyles.toolbarButton, GUILayout.Width(60)))
                SetAllFoldouts(true);
            if (GUILayout.Button("Collapse", EditorStyles.toolbarButton, GUILayout.Width(64)))
                SetAllFoldouts(false);

            GUILayout.Space(8);
            _search = EditorGUILayout.TextField(
                _search, EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(120), GUILayout.MaxWidth(280));

            GUILayout.FlexibleSpace();

            int migrations = 0;
            int issues = 0;
            for (int i = 0; i < _scopes.Count; i++)
            {
                var s = _scopes[i];
                migrations += s.Rows.Count;
                if (!string.IsNullOrEmpty(s.Error)) issues++;
                for (int r = 0; r < s.Rows.Count; r++)
                {
                    var st = s.Rows[r].Status;
                    if (st == RowStatus.Stale || st == RowStatus.Orphan) issues++;
                }
            }

            GUILayout.Label(
                "Scopes " + _scopes.Count + "  |  Migrations " + migrations + "  |  Issues " + issues,
                EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private void SetAllFoldouts(bool open)
        {
            for (int i = 0; i < _scopes.Count; i++)
                _foldouts[_scopes[i].Name] = open;
        }

        private bool DrawScope(ScopeView s, string filter)
        {
            bool hasContent = s.Rows.Count > 0 || s.Renames.Count > 0;
            bool hasError = !string.IsNullOrEmpty(s.Error);

            if (!hasContent && !hasError && !_showEmptyScopes) return false;

            bool scopeMatches = filter.Length == 0 || Contains(s.Name, filter);
            var visibleRows = new List<MigrationRow>();
            for (int i = 0; i < s.Rows.Count; i++)
            {
                if (scopeMatches || RowMatches(s.Rows[i], filter))
                    visibleRows.Add(s.Rows[i]);
            }

            if (filter.Length > 0 && !scopeMatches && visibleRows.Count == 0) return false;

            bool open = !_foldouts.TryGetValue(s.Name, out var stored) || stored;

            string title = s.Name + "   (" + s.Rows.Count
                + (s.Rows.Count == 1 ? " migration)" : " migrations)")
                + (s.IsAuto ? "   [auto-loaded]" : "");

            EditorGUILayout.Space(4);
            open = EditorGUILayout.Foldout(open, title, true, EditorStyles.foldoutHeader);
            _foldouts[s.Name] = open;

            if (!open) return true;

            EditorGUI.indentLevel++;

            if (hasError)
            {
                EditorGUILayout.HelpBox(
                    "Failed to build the definition of this scope.\n" + s.Error,
                    MessageType.Error);
            }
            else if (s.Rows.Count == 0)
            {
                EditorGUILayout.LabelField("(no migrations)", EditorStyles.miniLabel);
            }

            for (int i = 0; i < visibleRows.Count; i++)
                DrawRow(visibleRows[i]);

            if (s.Renames.Count > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("RenamedFrom (" + s.Renames.Count + ")", EditorStyles.boldLabel);
                for (int i = 0; i < s.Renames.Count; i++)
                {
                    var r = s.Renames[i];
                    string oldText = string.IsNullOrEmpty(r.OldFullName)
                        ? Hex(r.OldFullNameHash) : r.OldFullName;
                    string newText = string.IsNullOrEmpty(r.NewFullName)
                        ? Hex(r.NewFullNameHash) : r.NewFullName;
                    EditorGUILayout.SelectableLabel(
                        oldText + "  \u2192  " + newText,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
            }

            EditorGUI.indentLevel--;
            return true;
        }

        private void DrawRow(MigrationRow row)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            DrawBadge(row.Status);
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(row.TypeName) ? "(type name unresolved)" : row.TypeName,
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            DrawHash("NewTypeNameHash", row.Entry.NewTypeNameHash);
            DrawHash("OldSchemaHash", row.Entry.OldSchemaHash);
            DrawHash("NewSchemaHash", row.Entry.NewSchemaHash);

            switch (row.Status)
            {
                case RowStatus.Stale:
                    EditorGUILayout.HelpBox(
                        "NewSchemaHash does not match the current schema of this type (current: "
                        + Hex(row.CurrentSchemaHash)
                        + "). Loading data that uses this migration fails with SchemaMismatch.",
                        MessageType.Warning);
                    break;
                case RowStatus.Orphan:
                    EditorGUILayout.HelpBox(
                        "No module with this type name is registered in this scope.",
                        MessageType.Warning);
                    break;
                case RowStatus.Unknown:
                    EditorGUILayout.HelpBox(
                        "Modules are not bound for this scope, so the check is unavailable.",
                        MessageType.Info);
                    break;
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawBadge(RowStatus status)
        {
            string text;
            Color color;
            switch (status)
            {
                case RowStatus.Ok:
                    text = "OK";
                    color = new Color(0.35f, 0.8f, 0.4f);
                    break;
                case RowStatus.Stale:
                    text = "STALE";
                    color = new Color(1f, 0.75f, 0.2f);
                    break;
                case RowStatus.Orphan:
                    text = "ORPHAN";
                    color = new Color(1f, 0.45f, 0.35f);
                    break;
                default:
                    text = "?";
                    color = new Color(0.65f, 0.65f, 0.65f);
                    break;
            }

            var prev = GUI.contentColor;
            GUI.contentColor = color;
            GUILayout.Label(text, EditorStyles.miniBoldLabel, GUILayout.Width(56));
            GUI.contentColor = prev;
        }

        private static void DrawHash(string label, ulong value)
        {
            string hex = Hex(value);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(130));
            EditorGUILayout.SelectableLabel(hex, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(44)))
                EditorGUIUtility.systemCopyBuffer = hex;
            EditorGUILayout.EndHorizontal();
        }

        private static string Hex(ulong v) => "0x" + v.ToString("X16");

        private static bool Contains(string text, string filter)
            => !string.IsNullOrEmpty(text)
               && text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool RowMatches(MigrationRow row, string filter)
        {
            if (Contains(row.TypeName, filter)) return true;
            string f = filter.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? filter.Substring(2) : filter;
            if (f.Length == 0) return true;
            return Contains(Hex(row.Entry.NewTypeNameHash), f)
                || Contains(Hex(row.Entry.OldSchemaHash), f)
                || Contains(Hex(row.Entry.NewSchemaHash), f);
        }

        private void Refresh()
        {
            DestroyTempScopes();

            _scopes.Clear();
            _entries = new List<(string, MigrationEntry)>();
            _renames = new List<(string, RenamedEntry)>();
            _errorMessage = null;

            EnsureGeneratedBindings();

            var targets = new List<KeyValuePair<SaveScope, bool>>();
            var seen = new HashSet<Type>();

            var existing = Resources.FindObjectsOfTypeAll<SaveScope>();
            for (int i = 0; i < existing.Length; i++)
            {
                var scope = existing[i];
                if (scope == null) continue;
                if (scope.name == TempScopeName) continue;
                if (!seen.Add(scope.GetType())) continue;
                targets.Add(new KeyValuePair<SaveScope, bool>(scope, false));
            }

            if (_includeAllTypes)
            {
                foreach (var type in TypeCache.GetTypesDerivedFrom<SaveScope>())
                {
                    if (type == null || type.IsAbstract || type.IsGenericTypeDefinition) continue;
                    if (!seen.Add(type)) continue;

                    SaveScope temp = null;
                    try
                    {
                        temp = ScriptableObject.CreateInstance(type) as SaveScope;
                    }
                    catch
                    {
                        temp = null;
                    }

                    if (temp == null) continue;
                    temp.name = TempScopeName;
                    temp.hideFlags = HideFlags.HideAndDontSave;
                    _tempScopes.Add(temp);
                    targets.Add(new KeyValuePair<SaveScope, bool>(temp, true));
                }
            }

            if (targets.Count == 0)
            {
                _errorMessage = "No SaveScope instances found in the project.";
                return;
            }

            targets.Sort((a, b) => string.CompareOrdinal(
                a.Key.GetType().FullName, b.Key.GetType().FullName));

            for (int i = 0; i < targets.Count; i++)
            {
                var view = BuildScope(targets[i].Key, targets[i].Value);
                _scopes.Add(view);

                for (int r = 0; r < view.Rows.Count; r++)
                    _entries.Add((view.Name, view.Rows[r].Entry));
                for (int r = 0; r < view.Renames.Count; r++)
                    _renames.Add((view.Name, view.Renames[r]));
            }
        }

        private static ScopeView BuildScope(SaveScope scope, bool auto)
        {
            string name;
            try
            {
                name = scope.GetType().FullName;
            }
            catch
            {
                name = "(unknown)";
            }

            var view = new ScopeView { Name = name, IsAuto = auto };

            SaveDefinition def;
            try
            {
                def = scope.GetSaveSystem().Definition;
            }
            catch (Exception ex)
            {
                view.Error = ex.GetType().Name + ": " + ex.Message;
                return view;
            }

            if (def == null) return view;

            view.ModuleCount = def.Modules.Count;
            view.Bound = GeneratedScopeRegistry.TryGet(name, out _);

            var byNameHash = new Dictionary<ulong, SaveTypeModule>();
            for (int i = 0; i < def.Modules.Count; i++)
            {
                var m = def.Modules[i];
                byNameHash[SchemaHashCore.HashUtf8(m.FullTypeName)] = m;
            }

            bool canJudge = view.ModuleCount > 0 || view.Bound;

            var table = def.Migrations;
            if (table == null) return view;

            foreach (var entry in table.EnumerateEntries())
            {
                var row = new MigrationRow
                {
                    Entry = entry,
                    TypeName = entry.NewTypeName,
                };

                if (!canJudge)
                {
                    row.Status = RowStatus.Unknown;
                }
                else if (byNameHash.TryGetValue(entry.NewTypeNameHash, out var module))
                {
                    if (string.IsNullOrEmpty(row.TypeName)) row.TypeName = module.FullTypeName;
                    row.CurrentSchemaHash = module.SchemaHash;
                    row.Status = module.SchemaHash == entry.NewSchemaHash
                        ? RowStatus.Ok : RowStatus.Stale;
                }
                else
                {
                    row.Status = RowStatus.Orphan;
                }

                view.Rows.Add(row);
            }

            foreach (var rename in table.EnumerateRenamed())
                view.Renames.Add(rename);

            return view;
        }

        private void DestroyTempScopes()
        {
            for (int i = 0; i < _tempScopes.Count; i++)
            {
                if (_tempScopes[i] != null)
                    DestroyImmediate(_tempScopes[i]);
            }
            _tempScopes.Clear();
        }

        private static void EnsureGeneratedBindings()
        {
            if (s_bindingsEnsured) return;
            s_bindingsEnsured = true;

            foreach (var asm in UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies())
            {
                if (asm.IsDynamic) continue;

                string asmName = asm.GetName().Name;
                if (asmName.StartsWith("System", StringComparison.Ordinal)
                    || asmName.StartsWith("Unity", StringComparison.Ordinal)
                    || asmName.StartsWith("Mono", StringComparison.Ordinal)
                    || asmName.StartsWith("mscorlib", StringComparison.Ordinal)
                    || asmName.StartsWith("netstandard", StringComparison.Ordinal)
                    || asmName.StartsWith("nunit", StringComparison.Ordinal)
                    || asmName.StartsWith("Microsoft", StringComparison.Ordinal))
                    continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < types.Length; i++)
                {
                    var t = types[i];
                    if (t == null || t.Namespace != BindingsNamespace) continue;
                    if (!t.Name.EndsWith(RegistrationSuffix, StringComparison.Ordinal)) continue;

                    var method = t.GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic);
                    if (method == null) continue;

                    try
                    {
                        method.Invoke(null, null);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}