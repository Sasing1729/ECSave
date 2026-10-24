namespace ECSave
{
    /// <summary>
    /// Prefab 由来 Entity の識別情報。
    /// Save 時に Provider が返し、Load 時に現在の Provider が返す値と比較される。
    /// </summary>
    public readonly struct PrefabIdentityInfo
    {
        /// <summary>Prefab アセットの一意識別子（ユーザー定義）。0 は無効。</summary>
        public ulong PrefabGuid { get; }

        /// <summary>
        /// Prefab のベイク済みスキーマのハッシュ。
        /// 型レイアウトの変化を検出する。0 は未指定（検証スキップ）。
        /// </summary>
        public ulong SchemaHash { get; }

        public PrefabIdentityInfo(ulong prefabGuid, ulong schemaHash)
        {
            PrefabGuid = prefabGuid;
            SchemaHash = schemaHash;
        }
    }
}
