using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    public abstract class SaveTypeModuleShared<T> : SaveTypeModuleFixed<T>
        where T : unmanaged, ISharedComponentData
    {
        public override SectionKind SectionKind => SectionKind.Shared;

        protected override ComponentType QueryType => ComponentType.ReadWrite<T>();

        public override void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            => SnapshotCapture.CaptureChunksShared<T>(query, em, ref buffer);

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            T v = ModuleBytes.Read<T>(value, UnsafeUtility.SizeOf<T>());
            if (em.HasComponent<T>(entity))
                em.SetSharedComponent(entity, v);
            else
                em.AddSharedComponent(entity, v);
        }

        public override ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator)
        {
            RollbackHelper.BackupShared<T>(
                updateTargets, em, allocator, out var values, out var presence);
            return new ModuleBackup
            {
                Module = this,
                Values = values,
                Presence = presence,
                Enabled = default,
                Count = updateTargets.Length,
            };
        }

        public override void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em)
            => RollbackHelper.RestoreShared<T>(updateTargets, in backup, em);
    }
}
