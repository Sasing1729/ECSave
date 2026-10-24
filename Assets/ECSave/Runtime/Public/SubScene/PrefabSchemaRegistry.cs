using System.Collections.Generic;

namespace ECSave
{
    /// <summary>
    /// Prefab の現行スキーマハッシュを保持するグローバルレジストリ。
    /// </summary>
    public static class PrefabSchemaRegistry
    {
        private static readonly Dictionary<ulong, ulong> s_hashes
            = new Dictionary<ulong, ulong>();

        public static int Count => s_hashes.Count;

        public static void Register(ulong prefabGuid, ulong currentSchemaHash)
        {
            if (prefabGuid == 0UL) return;
            s_hashes[prefabGuid] = currentSchemaHash;
        }

        public static void Unregister(ulong prefabGuid)
            => s_hashes.Remove(prefabGuid);

        public static bool TryGet(ulong prefabGuid, out ulong currentSchemaHash)
            => s_hashes.TryGetValue(prefabGuid, out currentSchemaHash);

        public static void Clear() => s_hashes.Clear();

        /// <summary>
        /// Review #11: Domain Reload 無効時の静的状態リセット。
        /// セッション間で古いスキーマハッシュが残ると、ロード時に誤った
        /// マイグレーション判定が行われる。
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void ResetOnLoad()
        {
            s_hashes.Clear();
        }
    }
}
