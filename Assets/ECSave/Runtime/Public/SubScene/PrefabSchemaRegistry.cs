using System.Collections.Generic;

namespace ECSave
{
    /// <summary>
    /// Prefab の現行スキーマハッシュを保持するグローバルレジストリ。
    /// ゲームロジックは起動時（Prefab 登録時など）に Register を呼ぶ。
    /// D5.5（PrefabMigration 検証）はここから現行ハッシュを引く。
    ///
    /// 重要な注意:
    /// WithPrefabIdentityProvider を使いつつ PrefabMigration を登録した場合、
    /// 全ての移行対象 Prefab について必ず Register を呼ぶこと。
    /// 未登録の Prefab は ECSave がスキーマ検証・マイグレーションを実行できず、
    /// Deserialize 時に LoadWarningKind.PrefabSchemaNotRegistered 警告が発生する。
    /// ExtraHash に未登録の prefab GUID が格納される。
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
    }
}
