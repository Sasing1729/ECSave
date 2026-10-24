namespace ECSave
{
    public readonly struct SceneLoadRequest
    {
        public SceneGuid Guid { get; }
        public SceneLoadState TargetState { get; }
        public SceneRestoreMode Mode { get; }
        public ulong ContentHash { get; }

        public SceneLoadRequest(SceneGuid guid, SceneLoadState targetState, SceneRestoreMode mode)
            : this(guid, targetState, mode, 0UL)
        {
        }

        public SceneLoadRequest(SceneGuid guid, SceneLoadState targetState, SceneRestoreMode mode, ulong contentHash)
        {
            Guid = guid;
            TargetState = targetState;
            Mode = mode;
            ContentHash = contentHash;
        }
    }
}
