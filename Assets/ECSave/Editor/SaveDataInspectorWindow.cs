using UnityEditor;
using UnityEngine;

namespace ECSave.EditorTools
{
    public sealed class SaveDataInspectorWindow : EditorWindow
    {
        private string _filePath;
        private SaveDataInspection _inspection;
        private Vector2 _sectionScroll;
        private Vector2 _detailScroll;
        private int _selectedSection = -1;

        private HeaderView _headerView;
        private SectionTableView _sectionTableView;
        private TypeDescriptorView _typeDescriptorView;
        private ChunkComponentSectionView _chunkComponentView;
        private LargeDataSummaryView _largeDataSummaryView;

        [MenuItem("Window/ECSave/SaveData Inspector")]
        public static void Open()
        {
            var w = GetWindow<SaveDataInspectorWindow>();
            w.titleContent = new GUIContent("SaveData Inspector");
            w.minSize = new Vector2(900, 500);
        }

        private void OnEnable()
        {
            _headerView = new HeaderView();
            _sectionTableView = new SectionTableView();
            _typeDescriptorView = new TypeDescriptorView();
            _chunkComponentView = new ChunkComponentSectionView();
            _largeDataSummaryView = new LargeDataSummaryView();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_inspection == null)
            {
                EditorGUILayout.HelpBox("Open a SaveData file to inspect.", MessageType.Info);
                return;
            }

            if (!string.IsNullOrEmpty(_inspection.ErrorMessage))
            {
                EditorGUILayout.HelpBox(_inspection.ErrorMessage, MessageType.Error);
                return;
            }

            if (!_inspection.Loaded)
            {
                EditorGUILayout.HelpBox("File not loaded.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(320));
            _sectionScroll = EditorGUILayout.BeginScrollView(_sectionScroll);
            _selectedSection = _sectionTableView.Draw(_inspection, _selectedSection);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            _headerView.Draw(_inspection);
            EditorGUILayout.Space(8);
            DrawSelectedSection();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Open...", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                var path = EditorUtility.OpenFilePanel("Open SaveData", "", "sav,bin,dat,bytes");
                if (!string.IsNullOrEmpty(path))
                {
                    _filePath = path;
                    Reload();
                }
            }

            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(80)))
                Reload();

            GUILayout.Label(_filePath ?? "(no file)", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private void Reload()
        {
            _selectedSection = -1;
            _inspection = string.IsNullOrEmpty(_filePath) ? null : InspectionLoader.Load(_filePath);
        }

        private void DrawSelectedSection()
        {
            if (_selectedSection < 0 || _selectedSection >= _inspection.Sections.Length)
            {
                _largeDataSummaryView.Draw(_inspection);
                return;
            }

            var s = _inspection.Sections[_selectedSection];

            EditorGUILayout.LabelField("Section " + s.Index + ": " + s.Name, EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("SectionId", s.SectionId.ToString());
            EditorGUILayout.LabelField("Kind", s.Kind.ToString());
            EditorGUILayout.LabelField("NameHash", "0x" + s.NameHash.ToString("X16"));
            EditorGUILayout.LabelField("SchemaHash", "0x" + s.SchemaHash.ToString("X16"));
            EditorGUILayout.LabelField("Offset", s.Offset.ToString());
            EditorGUILayout.LabelField("Length", s.Length.ToString() + " bytes");
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            _typeDescriptorView.Draw(s);

            EditorGUILayout.Space(8);

            if (s.IsChunkComponent)
                _chunkComponentView.Draw(s);
        }
    }
}
