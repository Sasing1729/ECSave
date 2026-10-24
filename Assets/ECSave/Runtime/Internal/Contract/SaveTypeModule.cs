using System;
using ECSave.Format;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ECSave.Internal
{
    public abstract class SaveTypeModule
    {
        public int RegistrationId { get; internal set; }
        public abstract ulong SchemaHash { get; }
        public abstract string FullTypeName { get; }
        public abstract SectionKind SectionKind { get; }
        public abstract int RawRecordStride { get; }
        public abstract int RecordStride { get; }
        public abstract int[] EntityRefOffsets { get; }
        public abstract byte[] SchemaHashInput { get; }
        public abstract string[] FieldNames { get; }
        public abstract bool HasEnabledBits { get; }

        public virtual FixedStringFieldInfo[] FixedStringFields
            => Array.Empty<FixedStringFieldInfo>();

        public virtual FixedListPathLeafFieldInfo[] FixedListPathLeaves
            => Array.Empty<FixedListPathLeafFieldInfo>();

        /// <summary>
        /// このモジュールが「保存対象 Entity 集合」の構築に寄与するか。
        /// true: クエリ結果の Entity に永続 ID が割り当てられる（Component / Buffer / Shared / Identity）。
        /// false: クエリ結果は他モジュールが永続 ID を割り当てた Entity にのみ付与される（ChunkComponent）。
        /// </summary>
        public virtual bool ContributesToIdOf => true;

        public virtual bool SupportsAsyncCapture => false;

        public virtual JobHandle ScheduleAsyncCapture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer, JobHandle dependsOn)
        {
            Capture(query, em, ref buffer);
            return dependsOn;
        }

        public virtual void CompleteAsyncCapture(EntityManager em, ref SnapshotBuffer buffer) { }
        public virtual void CancelAsyncCapture() { }

        public abstract EntityQuery BuildQuery(EntityManager em);
        public abstract void Capture(EntityQuery query, EntityManager em, ref SnapshotBuffer buffer);
        public abstract int ComputeBodySize(in SnapshotBuffer buffer);
        public abstract void WriteSectionBody(
            ref FormatWriter writer, in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf);
        public abstract void ApplySectionBody(
            ByteView sectionBody, NativeArray<Entity> persistentToEntity, EntityManager em);

        public virtual void FilterAfterIdOf(
            ref SnapshotBuffer buffer, NativeParallelHashMap<Entity, int> idOf)
        { }

        public abstract void RemoveFromEntity(Entity entity, EntityManager em);

        public abstract ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator);

        public abstract void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em);

        protected virtual bool ReadEnabled(Entity entity, EntityManager em) => true;
        protected virtual void WriteEnabled(Entity entity, EntityManager em, bool enabled) { }
        protected virtual void ApplyEnabled(Entity entity, EntityManager em, bool enabled)
            => WriteEnabled(entity, em, enabled);
    }
}
