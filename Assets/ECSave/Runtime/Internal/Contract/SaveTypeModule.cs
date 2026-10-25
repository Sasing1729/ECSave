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

        /// <summary>
        /// セクションを World に復元する。
        ///
        /// <para>
        /// <b>実装ごとの契約</b>:
        /// <list type="bullet">
        ///   <item>
        ///     Component / Buffer / Shared / Identity —
        ///     セクションの値を各 Entity に書き込む。
        ///   </item>
        ///   <item>
        ///     ChunkComponent —
        ///     <b>チャンクコンポーネントの存在確保のみ</b> を行う。値の書き込みは
        ///     別途 <see cref="SaveTypeModuleChunkMarker.RestoreChunks"/> で行われる（2 フェーズ構成）。
        ///     詳細は <see cref="SaveTypeModuleChunk{T}.RestoreSection"/> を参照。
        ///   </item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>呼び出し順</b>: <c>DeserializeOperation</c> の P3〜P4 フェーズで、
        /// <see cref="SaveTypeModuleChunkMarker.RestoreChunks"/> より先に呼ばれる。
        /// </para>
        /// <para>
        /// <b>命名の経緯</b>: 旧名 <c>ApplySectionBody</c> は "Apply" が値の適用を強く連想させ、
        /// ChunkComponent の「存在確保のみ」と意味が食い違っていた。
        /// <c>RestoreSection</c> は「セーブデータから復元する」という文脈に沿い、
        /// 構造復元も値復元も包含できる。
        /// </para>
        /// </summary>
        public abstract void RestoreSection(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em);

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
