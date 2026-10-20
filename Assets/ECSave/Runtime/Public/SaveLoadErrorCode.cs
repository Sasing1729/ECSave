namespace ECSave
{
    /// <summary>
    /// Save / Load の失敗理由（外部設計 13.3）。
    /// 裏付け型は int。Burst ジョブへは ushort として渡す（15.2節）。
    /// </summary>
    public enum SaveLoadErrorCode
    {
        None = 0,
        CorruptedData = 1,
        ChecksumMismatch = 2,
        VersionMismatch = 3,
        SchemaMismatch = 4,
        MigrationFailed = 5,
        ValidationFailed = 6,
        EntityReferenceInvalid = 7,
        EntityGenerationFailed = 8,
        WorldMismatch = 9,
        ComponentApplyFailed = 10,
        SharedComponentApplyFailed = 11,
        BufferApplyFailed = 12,
        ConcurrentOperation = 13,
        WorldModificationFailed = 14,
        Cancelled = 15,
        SerializationFailed = 16,

        // SubScene 関連（Phase S1a 以降）
        SceneLoadFailed = 17,
        SceneContentMismatch = 18,
        PrefabMigrationFailed = 19,
    }
}
