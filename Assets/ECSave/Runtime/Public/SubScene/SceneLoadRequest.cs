namespace ECSave
{
    public readonly struct SceneLoadRequest
    {
        public SceneGuid Guid { get; }
        public SceneLoadState TargetState { get; }
        public SceneRestoreMode Mode { get; }

        public SceneLoadRequest(SceneGuid guid, SceneLoadState targetState, SceneRestoreMode mode)
        {
            Guid = guid;
            TargetState = targetState;
            Mode = mode;
        }
    }
}
