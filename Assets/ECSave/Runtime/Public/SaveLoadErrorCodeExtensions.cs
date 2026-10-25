namespace ECSave
{
    /// <summary>
    /// <see cref="SaveLoadErrorCode"/> のユーザー向け説明を提供する拡張メソッド。
    ///
    /// <para>
    /// <b>用途</b>: UI 表示、ログ出力、開発時診断など、コード単体では意味が伝わりにくい場面を補助する。
    /// </para>
    /// <para>
    /// <b>言語</b>: 英語で統一。多言語対応が必要な場合は、利用側で
    /// <c>SaveLoadErrorCode</c> の <c>ToString()</c> をキーに独自ローカライズを引くこと。
    /// </para>
    /// <para>
    /// <b><see cref="SaveLoadErrorCode.None"/> の扱い</b>: 2 メソッド（
    /// <see cref="GetDescription"/> / <see cref="GetRemediation"/>）とも空文字列を返す。
    /// 「エラーが発生していない」状態に説明・対処は無いため。
    /// </para>
    /// </summary>
    public static class SaveLoadErrorCodeExtensions
    {
        /// <summary>何が起きたかの説明。<see cref="SaveLoadErrorCode.None"/> では空文字列。</summary>
        public static string GetDescription(this SaveLoadErrorCode code)
            => code switch
            {
                SaveLoadErrorCode.None =>
                    string.Empty,
                SaveLoadErrorCode.CorruptedData =>
                    "The save data is structurally corrupted or has an invalid layout.",
                SaveLoadErrorCode.ChecksumMismatch =>
                    "The save data checksum does not match. The data is corrupted or was modified.",
                SaveLoadErrorCode.VersionMismatch =>
                    "The save data format version or game data version is newer than the runtime supports.",
                SaveLoadErrorCode.SchemaMismatch =>
                    "The save data schema does not match any registered module.",
                SaveLoadErrorCode.MigrationFailed =>
                    "A migration step failed while converting the save data.",
                SaveLoadErrorCode.ValidationFailed =>
                    "The save data failed content validation (out-of-range values, duplicate keys, etc.).",
                SaveLoadErrorCode.EntityReferenceInvalid =>
                    "The save data contains an entity reference that cannot be resolved.",
                SaveLoadErrorCode.EntityGenerationFailed =>
                    "Failed to generate entities from the save data.",
                SaveLoadErrorCode.WorldMismatch =>
                    "The save data was created for a different world.",
                SaveLoadErrorCode.ComponentApplyFailed =>
                    "Failed to apply a component value.",
                SaveLoadErrorCode.SharedComponentApplyFailed =>
                    "Failed to apply a shared component value.",
                SaveLoadErrorCode.BufferApplyFailed =>
                    "Failed to apply a buffer value.",
                SaveLoadErrorCode.ConcurrentOperation =>
                    "Another save/load operation is already in progress for this world.",
                SaveLoadErrorCode.WorldModificationFailed =>
                    "A structural modification to the world failed; the world may have been rolled back.",
                SaveLoadErrorCode.Cancelled =>
                    "The operation was cancelled.",
                SaveLoadErrorCode.SerializationFailed =>
                    "Serialization failed.",
                SaveLoadErrorCode.SceneLoadFailed =>
                    "A SubScene restore request failed.",
                SaveLoadErrorCode.SceneContentMismatch =>
                    "The SubScene content hash does not match the saved state.",
                SaveLoadErrorCode.PrefabMigrationFailed =>
                    "A prefab migration step failed.",
                _ =>
                    "Unknown error.",
            };

        /// <summary>次に何をすべきかのガイド。<see cref="SaveLoadErrorCode.None"/> では空文字列。</summary>
        public static string GetRemediation(this SaveLoadErrorCode code)
            => code switch
            {
                SaveLoadErrorCode.None =>
                    string.Empty,
                SaveLoadErrorCode.CorruptedData =>
                    "Restore from a backup save. If none is available, start a new game.",
                SaveLoadErrorCode.ChecksumMismatch =>
                    "Restore from a backup save. If none is available, start a new game.",
                SaveLoadErrorCode.VersionMismatch =>
                    "Update the runtime (ECSave.dll) to a version that supports this save data.",
                SaveLoadErrorCode.SchemaMismatch =>
                    "Add a MigrationStep for the changed type, or register a RenamedFrom alias if the type was renamed.",
                SaveLoadErrorCode.MigrationFailed =>
                    "Check the migration implementation; enable ECSave diagnostics to inspect the failed phase.",
                SaveLoadErrorCode.ValidationFailed =>
                    "The save data may be malicious or corrupted; inspect the reported field path.",
                SaveLoadErrorCode.EntityReferenceInvalid =>
                    "The save data references an entity that does not exist in the current save; " +
                    "the saved SaveDefinition may differ from the current one.",
                SaveLoadErrorCode.EntityGenerationFailed =>
                    "Verify that the SaveDefinition is complete and matches the target world's archetypes.",
                SaveLoadErrorCode.WorldMismatch =>
                    "Load the save data into a world with the same name, or set the WorldId explicitly in SaveDefinition.",
                SaveLoadErrorCode.ComponentApplyFailed =>
                    "Check the component type's layout: LayoutKind.Sequential and public/internal fields are required.",
                SaveLoadErrorCode.SharedComponentApplyFailed =>
                    "Check the shared component type's layout: LayoutKind.Sequential and public/internal fields are required.",
                SaveLoadErrorCode.BufferApplyFailed =>
                    "Check the buffer element type's layout: LayoutKind.Sequential and public/internal fields are required.",
                SaveLoadErrorCode.ConcurrentOperation =>
                    "Await the previous operation before starting a new one, or serialize access from a single system.",
                SaveLoadErrorCode.WorldModificationFailed =>
                    "The world may be in a partially-modified state; reload from a known good state if possible.",
                SaveLoadErrorCode.Cancelled =>
                    "The operation was cancelled; no changes were applied.",
                SaveLoadErrorCode.SerializationFailed =>
                    "Check the world and SaveDefinition; see the debug log for details.",
                SaveLoadErrorCode.SceneLoadFailed =>
                    "Verify that the SceneRestoreCallback is implemented and that the requested scenes are available.",
                SaveLoadErrorCode.SceneContentMismatch =>
                    "The currently loaded SubScene content differs from the saved state; review your asset content.",
                SaveLoadErrorCode.PrefabMigrationFailed =>
                    "Check the prefab migration implementation; see the debug log for the failing prefab GUID and schema hash.",
                _ =>
                    string.Empty,
            };

        /// <summary>
        /// UI 表示用に「説明 + 対処」を 1 行にまとめる。
        /// <see cref="SaveLoadErrorCode.None"/> の場合は空文字列。
        /// </summary>
        public static string GetUserFacingMessage(this SaveLoadErrorCode code)
        {
            if (code == SaveLoadErrorCode.None) return string.Empty;
            var desc = code.GetDescription();
            var rem = code.GetRemediation();
            return string.IsNullOrEmpty(rem) ? desc : $"{desc} {rem}";
        }
    }
}
