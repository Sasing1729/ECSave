// Runtime/Phase/Identity/IdentityEntry.cs
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// 識別キー索引のエントリ（9.1節）。
    /// World 側と SaveData 側で共用する。使わないフィールドは既定値のまま。
    /// </summary>
    public struct IdentityEntry
    {
        public uint TypeIndex;
        public int ArenaOffset;
        public int Length;

        public Entity WorldEntity;         // World 側のみ
        public int SavePersistentId;       // SaveData 側のみ。それ以外は -1

        public bool IsWorldSide => SavePersistentId < 0;
    }
}
