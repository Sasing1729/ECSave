using System;
using ECSave.Format;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal unsafe struct PrephaseContext : IDisposable
    {
        public SaveDefinition Definition;
        public World World;

        public NativeArray<byte> NativeData;
        public ByteView View;

        public SaveHeader Header;
        public SectionTable SectionTable;
        public SaveTypeModule[] ModuleBySection;

        public NativeArray<Entity> PersistentToEntity;
        public WarningCollector Warnings;

        public static PrephaseContext Create(SaveDefinition def, World world)
        {
            return new PrephaseContext
            {
                Definition = def,
                World = world,
                Warnings = new WarningCollector(Allocator.Persistent),
            };
        }

        public void Dispose()
        {
            if (NativeData.IsCreated) NativeData.Dispose();
            if (PersistentToEntity.IsCreated) PersistentToEntity.Dispose();
            Warnings.Dispose();
            NativeData = default;
            PersistentToEntity = default;
        }
    }
}
