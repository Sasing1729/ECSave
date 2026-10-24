using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ECSave.Internal
{
    public abstract class SaveTypeModule<T> : SaveTypeModuleFixed<T>
        where T : unmanaged, IComponentData
    {
        public override ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator)
            => RollbackHelper.BackupFixed<T>(
                this, updateTargets, em, allocator, HasEnabledBits, ReadEnabled);

        public override void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em)
            => RollbackHelper.RestoreFixed<T>(
                updateTargets, in backup, em, HasEnabledBits, WriteEnabled);
    }

    public abstract class SaveTypeModulePlain<T> : SaveTypeModule<T>
        where T : unmanaged, IComponentData
    {
        private NativeList<Entity> _asyncEntities;
        private NativeList<T> _asyncValues;

        public override bool SupportsAsyncCapture => true;

        public override JobHandle ScheduleAsyncCapture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer, JobHandle dependsOn)
        {
            CancelAsyncCapture();
            _asyncEntities = query.ToEntityListAsync(Allocator.Persistent, out var h1);
            _asyncValues = query.ToComponentDataListAsync<T>(Allocator.Persistent, out var h2);
            return JobHandle.CombineDependencies(dependsOn, h1, h2);
        }

        public override void CompleteAsyncCapture(EntityManager em, ref SnapshotBuffer buffer)
        {
            if (!_asyncEntities.IsCreated) return;
            try { buffer.BulkAppendPlain(_asyncEntities.AsArray(), _asyncValues.AsArray()); }
            finally { CancelAsyncCapture(); }
        }

        public override void CancelAsyncCapture()
        {
            if (_asyncEntities.IsCreated) { _asyncEntities.Dispose(); _asyncEntities = default; }
            if (_asyncValues.IsCreated) { _asyncValues.Dispose(); _asyncValues = default; }
        }

        public override void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            => SnapshotCapture.CaptureChunksPlain<T>(query, em, ref buffer);
    }

    public abstract class SaveTypeModuleEnableable<T> : SaveTypeModule<T>
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        public override bool HasEnabledBits => true;

        private NativeList<Entity> _asyncEntities;
        private NativeList<T> _asyncValues;

        public override bool SupportsAsyncCapture => true;

        public override JobHandle ScheduleAsyncCapture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer, JobHandle dependsOn)
        {
            CancelAsyncCapture();
            _asyncEntities = query.ToEntityListAsync(Allocator.Persistent, out var h1);
            _asyncValues = query.ToComponentDataListAsync<T>(Allocator.Persistent, out var h2);
            return JobHandle.CombineDependencies(dependsOn, h1, h2);
        }

        public unsafe override void CompleteAsyncCapture(EntityManager em, ref SnapshotBuffer buffer)
        {
            if (!_asyncEntities.IsCreated) return;
            try
            {
                // フレーム跨ぎで破棄された Entity がある場合は個別処理にフォールバック
                bool anyStale = false;
                for (int i = 0; i < _asyncEntities.Length; i++)
                {
                    var e = _asyncEntities[i];
                    if (e == Entity.Null || !em.Exists(e)) { anyStale = true; break; }
                }

                if (anyStale || _asyncValues.Length != _asyncEntities.Length)
                {
                    for (int i = 0; i < _asyncEntities.Length; i++)
                    {
                        var e = _asyncEntities[i];
                        if (e == Entity.Null || !em.Exists(e)) continue;
                        var v = em.GetComponentData<T>(e);
                        buffer.Append(e, (byte*)&v);
                        if (HasEnabledBits)
                            buffer.AppendEnabled(em.IsComponentEnabled<T>(e));
                    }
                    return;
                }

                buffer.BulkAppendPlain(_asyncEntities.AsArray(), _asyncValues.AsArray());
                if (HasEnabledBits)
                {
                    for (int i = 0; i < _asyncEntities.Length; i++)
                        buffer.AppendEnabled(em.IsComponentEnabled<T>(_asyncEntities[i]));
                }
            }
            finally { CancelAsyncCapture(); }
        }

        public override void CancelAsyncCapture()
        {
            if (_asyncEntities.IsCreated) { _asyncEntities.Dispose(); _asyncEntities = default; }
            if (_asyncValues.IsCreated) { _asyncValues.Dispose(); _asyncValues = default; }
        }

        public override void Capture(EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            if (HasEnabledBits)
                SnapshotCapture.CaptureChunksEnableable<T>(query, em, ref buffer);
            else
                SnapshotCapture.CaptureChunksPlain<T>(query, em, ref buffer);
        }

        protected override bool ReadEnabled(Entity entity, EntityManager em)
            => em.IsComponentEnabled<T>(entity);

        protected override void WriteEnabled(Entity entity, EntityManager em, bool enabled)
            => em.SetComponentEnabled<T>(entity, enabled);
    }
}
