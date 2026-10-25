using System.Collections.Generic;

namespace ECSave
{
    /// <summary>
    /// Prefab の現行スキーマハッシュを保持するグローバルレジストリ。
    ///
    /// <para>
    /// <b>用途</b>: <c>D5_5_PrefabMigrationPhase</c> が保存時と現在のスキーマハッシュを
    /// 比較し、変化があれば <c>PrefabMigrationStepBase</c> のチェーンを解決する。
    /// ユーザーコードが起動時に <see cref="Register"/> を呼び、Prefab GUID と
    /// 現在のスキーマハッシュを関連付ける。
    /// </para>
    /// <para>
    /// <b>スコープ</b>: グローバル（静的）。複数 World で共有される。
    /// <see cref="SubSceneTombstone"/> / <see cref="AssetReferenceRegistry"/> と同じ方針。
    /// </para>
    /// <para>
    /// <b>上書き</b>: 同じ GUID で <see cref="Register"/> を複数回呼ぶと、
    /// 最後の値で上書きされる。Prefab のホットリロード時はこれを使う。
    /// </para>
    /// <para>
    /// <b>Domain Reload 無効時</b>: <see cref="UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded"/>
    /// で自動クリアされる。
    /// </para>
    /// </summary>
    public static class PrefabSchemaRegistry
    {
        private static readonly Dictionary<ulong, ulong> s_hashes
            = new Dictionary<ulong, ulong>();

        /// <summary>
        /// Prefab GUID と現在のスキーマハッシュを登録する。
        ///
        /// <para>
        /// <paramref name="prefabGuid"/> が 0 の場合は何もしない（無効値）。
        /// 同じ GUID で複数回呼ぶと、最後の値で上書きされる。
        /// </para>
        /// </summary>
        public static void Register(ulong prefabGuid, ulong currentSchemaHash)
        {
            if (prefabGuid == 0UL) return;
            s_hashes[prefabGuid] = currentSchemaHash;
        }

        /// <summary>
        /// Prefab GUID から現在のスキーマハッシュを取得する。
        /// 未登録なら <c>false</c>。
        /// </summary>
        public static bool TryGet(ulong prefabGuid, out ulong currentSchemaHash)
            => s_hashes.TryGetValue(prefabGuid, out currentSchemaHash);

        /// <summary>
        /// 全登録をクリアする。
        ///
        /// <para>
        /// <b>テスト・エディタツール専用</b>。<see cref="UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded"/>
        /// の <see cref="ResetOnLoad"/> からも呼ばれる。
        /// ユーザーコードからは呼ばないこと。
        /// </para>
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        internal static void Clear()
        {
            s_hashes.Clear();
        }

        /// <summary>
        /// Domain Reload 無効時の静的状態リセット。
        /// セッション間で古いスキーマハッシュが残ると、ロード時に誤った
        /// マイグレーション判定が行われる。
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void ResetOnLoad()
        {
            Clear();
        }
    }
}
