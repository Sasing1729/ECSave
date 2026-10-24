using System;
using Unity.Entities;

namespace ECSave
{
    public interface IPrefabMigration
    {
        ulong OldPrefabSchemaHash { get; }
        ulong NewPrefabSchemaHash { get; }
        void Migrate(ref PrefabMigrationContext context);
    }

    /// <summary>
    /// Prefab 差し替え時の Migration コンテキスト。
    /// EntityManager / Entity を使って、World 上の Entity を直接操作できる。
    /// </summary>
    public ref struct PrefabMigrationContext
    {
        // === S2b-2 追加 ===
        public EntityManager EntityManager;
        public Entity Entity;
        public ulong SourcePrefabGuid;
        public ulong TargetPrefabGuid;
        public ulong SourceSchemaHash;
        public ulong TargetSchemaHash;
        // === ここまで ===

        // === 後方互換（ECS内部では未使用） ===
        public ReadOnlySpan<byte> OldData;
        public Span<byte> NewBuffer;
        public int NewLength;
        // === ここまで ===
    }
}
