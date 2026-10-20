namespace ECSave
{
    public enum ContentVerificationMode : byte
    {
        None = 0,
        GuidOnly = 1,
        GuidAndContentHash = 2,
    }

    public enum StreamingConsistencyMode : byte
    {
        TimeBased = 0,
        FrameBoundary = 1,
    }

    public enum SceneLoadState : byte
    {
        Unloaded = 0,
        Loaded = 1,
        Loading = 2,
    }

    public enum SceneRestoreMode : byte
    {
        Required = 0,
        Optional = 1,
    }
}
