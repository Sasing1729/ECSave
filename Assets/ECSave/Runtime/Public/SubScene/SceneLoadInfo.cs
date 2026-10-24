namespace ECSave
{
    public readonly struct SceneLoadInfo
    {
        public SceneGuid Guid { get; }
        public SceneLoadState State { get; }
        public SceneRestoreMode RestoreMode { get; }

        /// <summary>
        /// ベイク済み SubScene コンテンツのハッシュ（任意）。
        /// 0 は「未知」を意味し、ContentVerificationMode.GuidAndContentHash でも検証をスキップする。
        /// ハッシュの算出方法はゲームロジックの責務（例: ベイク結果の Entity 数・Archetype 構成のハッシュ）。
        /// </summary>
        public ulong ContentHash { get; }

        public SceneLoadInfo(SceneGuid guid, SceneLoadState state, SceneRestoreMode restoreMode = SceneRestoreMode.Required)
            : this(guid, state, restoreMode, 0UL)
        {
        }

        public SceneLoadInfo(SceneGuid guid, SceneLoadState state, SceneRestoreMode restoreMode, ulong contentHash)
        {
            Guid = guid;
            State = state;
            RestoreMode = restoreMode;
            ContentHash = contentHash;
        }
    }
}
