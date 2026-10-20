using System;

namespace ECSave
{
    public interface IPrefabMigration
    {
        ulong OldPrefabSchemaHash { get; }
        ulong NewPrefabSchemaHash { get; }
        void Migrate(ref PrefabMigrationContext context);
    }

    public ref struct PrefabMigrationContext
    {
        public ReadOnlySpan<byte> OldData;
        public Span<byte> NewBuffer;
        public int NewLength;
    }
}
