namespace ECSave
{
    public enum LoadWarningKind : byte
    {
        EntityReferenceUnresolved = 0,
        UnknownSectionIgnored = 1,
        ChunkComponentMerged = 2,
        SceneLoadStateMismatch = 3,
        SceneRestoreFailed = 4,
        SceneContentHashMismatch = 5,
        PrefabMigrated = 6,
        SceneStateRolledBack = 7,
    }
}
