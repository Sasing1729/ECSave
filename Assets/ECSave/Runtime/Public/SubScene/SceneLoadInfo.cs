namespace ECSave
{
    public readonly struct SceneLoadInfo
    {
        public SceneGuid Guid { get; }
        public SceneLoadState State { get; }
        public SceneRestoreMode RestoreMode { get; }

        public SceneLoadInfo(SceneGuid guid, SceneLoadState state, SceneRestoreMode restoreMode = SceneRestoreMode.Required)
        {
            Guid = guid;
            State = state;
            RestoreMode = restoreMode;
        }
    }
}
