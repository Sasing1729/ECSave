using System;
using System.Collections.Generic;
using ECSave.Format;
using ECSave.Internal.SubScene;
using ECSave.Phase.Deserialize.Prephase;
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
        public NativeParallelHashSet<Entity>[] RegisteredTargets;

        public SubSceneStateSectionView? SubSceneState;
        public SceneLoadRequest[] SubSceneRestoreRequests;
        public IReadOnlyList<D8_5_TombstoneValidationPhase.TombstoneEntry> TombstoneSections;
        public PrefabIdentitySectionView? PrefabIdentities;

        // === S2b-2 追加 ===
        public List<PrefabMigrationEntry> PrefabMigrationsToApply;
        // === ここまで ===

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

            if (RegisteredTargets != null)
            {
                for (int i = 0; i < RegisteredTargets.Length; i++)
                {
                    if (RegisteredTargets[i].IsCreated)
                        RegisteredTargets[i].Dispose();
                }
                RegisteredTargets = null;
            }

            Warnings.Dispose();
            NativeData = default;
            PersistentToEntity = default;
            TombstoneSections = null;
            PrefabIdentities = null;
            PrefabMigrationsToApply = null;
        }
    }
}
