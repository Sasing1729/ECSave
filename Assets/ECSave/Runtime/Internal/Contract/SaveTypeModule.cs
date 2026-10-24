using System;
using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using FormatError = ECSave.Format.FormatError;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal
{
    internal static class ModuleBytes
    {
        public static unsafe T Read<T>(byte* src, int length) where T : unmanaged
        {
            T value = default;
            UnsafeUtility.MemCpy(&value, src, length);
            return value;
        }

        public static unsafe void Write<T>(T value, byte* dst) where T : unmanaged
            => UnsafeUtility.MemCpy(dst, &value, UnsafeUtility.SizeOf<T>());
    }

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
        /// このモジュールが「保存対象 Entity 集合（= _idOf）」の構築に寄与するか。
        ///
        /// <para>
        /// <b>true</b>（デフォルト）: モジュールのクエリ結果に含まれる Entity は
        /// 自動的に永続 ID が割り当てられ、保存対象になる。Component / Buffer /
        /// Shared / Identity は通常こちら。
        /// </para>
        ///
        /// <para>
        /// <b>false</b>: クエリ結果は保存対象にはならず、他のモジュール
        /// （または <see cref="SelectionModule"/>）が既に永続 ID を割り当てた
        /// Entity にのみ付与される。ChunkComponent はこちら。
        /// </para>
        ///
        /// <para>
        /// <b>ChunkComponent が false である理由</b>: チャンクコンポーネントは
        /// Entity 単位ではなくチャンク単位の属性であり、「チャンクコンポーネント
        /// だけを持つ Entity」という概念が無い。チャンクに属する Entity は
        /// 通常のコンポーネント（Health など）または識別型（PlayerId など）で
        /// 既に保存対象になっている必要がある。
        /// </para>
        ///
        /// <para>
        /// <b>副作用の例</b>: チャンクコンポーネントのみを持つ Entity は、
        /// 他のモジュールが登録されていない限り保存対象にならない。
        /// そのため、ロード後の World に該当 Entity が復元されない
        /// （<see cref="SaveTypeModuleChunk{T}.FilterAfterIdOf"/> で
        /// 該当グループが削除される）。
        /// </para>
        /// </summary>
        public virtual bool ContributesToIdOf => true;

        public virtual bool SupportsAsyncCapture => false;

        public virtual JobHandle ScheduleAsyncCapture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer, JobHandle dependsOn)
        {
            Capture(query, em, ref buffer);
            return dependsOn;
        }

        public virtual void CompleteAsyncCapture(EntityManager em, ref SnapshotBuffer buffer)
        {
        }

        public virtual void CancelAsyncCapture()
        {
        }

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

    public abstract class SaveTypeModuleBase<T> : SaveTypeModule
        where T : unmanaged
    {
        public override int RawRecordStride => UnsafeUtility.SizeOf<T>();
        public override int[] EntityRefOffsets => ModuleShared.EmptyInts;
        public override int RecordStride => UnsafeUtility.SizeOf<T>() - 4 * EntityRefOffsets.Length;
        public override bool HasEnabledBits => false;
        public override SectionKind SectionKind => SectionKind.Component;

        protected virtual ComponentType QueryType => ComponentType.ReadOnly<T>();
        protected virtual EntityQueryOptions QueryOptions => EntityQueryOptions.IgnoreComponentEnabledState;

        public override EntityQuery BuildQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { QueryType },
                Options = QueryOptions,
            });

        public override void RemoveFromEntity(Entity entity, EntityManager em)
            => ModuleShared.RemoveComponent<T>(entity, em);

        protected bool WritesEnabledBits(in SnapshotBuffer buffer)
            => HasEnabledBits && buffer.HasEnabledStates;
    }

    public abstract class SaveTypeModuleFixed<T> : SaveTypeModuleBase<T>
        where T : unmanaged
    {
        public override int ComputeBodySize(in SnapshotBuffer buffer)
            => CommonSectionWriter.ComputeFixedBodySize(
                buffer.Count, RecordStride,
                hasEnabledBits: WritesEnabledBits(in buffer));

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int count = buffer.Count;
            bool hasEnabled = WritesEnabledBits(in buffer);
            int rawStride = RawRecordStride;
            int dataStride = RecordStride;
            var refs = EntityRefOffsets;

            SectionBodyHelper.WriteFixedHeader(ref writer, count, dataStride, hasEnabled);
            SectionBodyHelper.WriteEntityIndices(ref writer, in buffer, idOf);

            if (hasEnabled)
                SectionBodyHelper.WriteEnabledBits(ref writer, in buffer, count);

            if (dataStride <= 0 || count == 0) return;

            if (refs.Length == 0)
            {
                byte* src = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();
                writer.WriteBytes(src, count * rawStride);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    EntityRefCodec.WriteRecordWithRefs(
                        ref writer, buffer.GetValuePtr(i), refs, rawStride, idOf);
            }
        }

        public unsafe override void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            ModuleShared.ApplyFixedSection(
                sectionBody, RecordStride, RawRecordStride, EntityRefOffsets, HasEnabledBits,
                persistentToEntity, em, WriteValue, ApplyEnabled);
        }

        protected abstract unsafe void WriteValue(Entity entity, EntityManager em, byte* value);
    }

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

            try
            {
                buffer.BulkAppendPlain(_asyncEntities.AsArray(), _asyncValues.AsArray());
            }
            finally
            {
                CancelAsyncCapture();
            }
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
                if (_asyncValues.Length != _asyncEntities.Length)
                {
                    for (int i = 0; i < _asyncEntities.Length; i++)
                    {
                        var e = _asyncEntities[i];
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
            finally
            {
                CancelAsyncCapture();
            }
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

    public abstract class SaveTypeModuleBuffer<T> : SaveTypeModuleBase<T>
        where T : unmanaged, IBufferElementData
    {
        public override SectionKind SectionKind => SectionKind.Buffer;

        private NativeArray<ArchetypeChunk> _asyncChunks;
        private NativeArray<int> _chunkStartIndices;
        private NativeArray<Entity> _asyncEntities;
        private NativeArray<int> _asyncElementCounts;
        private NativeArray<int> _asyncByteOffsets;
        private NativeArray<byte> _asyncOutput;
        private EntityQuery _asyncQuery;

        public override bool SupportsAsyncCapture => true;

        protected NativeArray<Entity> AsyncEntities => _asyncEntities;

        public override void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            buffer.InitRecordLengths(Allocator.TempJob);
            if (HasEnabledBits)
                buffer.InitEnabledStates(Allocator.TempJob);
            CaptureImpl(query, em, ref buffer);
        }

        protected virtual void CaptureImpl(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            => SnapshotCapture.CaptureChunksBuffer<T>(query, em, ref buffer);

        protected virtual JobHandle ScheduleCountJob(
            BufferTypeHandle<T> bufferHandle, BufferAsyncContext ctx, JobHandle deps)
        {
            return new BufferCountJob<T>
            {
                BufferHandle = bufferHandle,
                EntityHandle = ctx.EntityManager.GetEntityTypeHandle(),
                ChunkStartIndices = ctx.ChunkStartIndices,
                Entities = ctx.Entities,
                ElementCounts = ctx.ElementCounts,
            }.ScheduleParallel(ctx.Query, deps);
        }

        protected virtual JobHandle ScheduleCopyJob(
            BufferTypeHandle<T> bufferHandle, BufferAsyncContext ctx, JobHandle deps)
        {
            return new BufferCopyJob<T>
            {
                BufferHandle = bufferHandle,
                ChunkStartIndices = ctx.ChunkStartIndices,
                ByteOffsets = ctx.ByteOffsets,
                Output = ctx.Output,
                Stride = ctx.Stride,
            }.ScheduleParallel(ctx.Query, deps);
        }

        public override JobHandle ScheduleAsyncCapture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer, JobHandle dependsOn)
        {
            CancelAsyncCapture();

            buffer.InitRecordLengths(Allocator.Persistent);

            _asyncQuery = query;
            _asyncChunks = query.ToArchetypeChunkArray(Allocator.Persistent);

            int chunkCount = _asyncChunks.Length;
            _chunkStartIndices = new NativeArray<int>(chunkCount, Allocator.Persistent);

            int totalEntities = 0;
            for (int i = 0; i < chunkCount; i++)
            {
                _chunkStartIndices[i] = totalEntities;
                totalEntities += _asyncChunks[i].Count;
            }

            _asyncEntities = new NativeArray<Entity>(totalEntities, Allocator.Persistent);
            _asyncElementCounts = new NativeArray<int>(totalEntities, Allocator.Persistent);

            if (totalEntities == 0)
                return dependsOn;

            var bufferHandle = em.GetBufferTypeHandle<T>(true);
            var ctx = new BufferAsyncContext
            {
                Query = query,
                EntityManager = em,
                ChunkStartIndices = _chunkStartIndices,
                Entities = _asyncEntities,
                ElementCounts = _asyncElementCounts,
                ByteOffsets = default,
                Output = default,
                Stride = RawRecordStride,
            };

            return ScheduleCountJob(bufferHandle, ctx, dependsOn);
        }

        public override void CompleteAsyncCapture(EntityManager em, ref SnapshotBuffer buffer)
        {
            if (!_asyncChunks.IsCreated) return;

            try
            {
                int entityCount = _asyncEntities.Length;
                int stride = RawRecordStride;

                _asyncByteOffsets = new NativeArray<int>(entityCount, Allocator.Persistent);

                int totalBytes = 0;
                for (int i = 0; i < entityCount; i++)
                {
                    _asyncByteOffsets[i] = totalBytes;
                    totalBytes += _asyncElementCounts[i] * stride;
                }

                _asyncOutput = new NativeArray<byte>(totalBytes, Allocator.Persistent);

                if (totalBytes > 0)
                {
                    var bufferHandle = em.GetBufferTypeHandle<T>(true);
                    var ctx = new BufferAsyncContext
                    {
                        Query = _asyncQuery,
                        EntityManager = em,
                        ChunkStartIndices = _chunkStartIndices,
                        Entities = _asyncEntities,
                        ElementCounts = _asyncElementCounts,
                        ByteOffsets = _asyncByteOffsets,
                        Output = _asyncOutput,
                        Stride = stride,
                    };

                    var pass2 = ScheduleCopyJob(bufferHandle, ctx, default);
                    pass2.Complete();
                }

                buffer.BulkAppendVariable(_asyncEntities, _asyncElementCounts, _asyncOutput);

                AppendEnabledStates(em, ref buffer);
            }
            finally
            {
                CancelAsyncCapture();
            }
        }

        protected virtual void AppendEnabledStates(EntityManager em, ref SnapshotBuffer buffer) { }

        public override void CancelAsyncCapture()
        {
            if (_asyncChunks.IsCreated) { _asyncChunks.Dispose(); _asyncChunks = default; }
            if (_chunkStartIndices.IsCreated) { _chunkStartIndices.Dispose(); _chunkStartIndices = default; }
            if (_asyncEntities.IsCreated) { _asyncEntities.Dispose(); _asyncEntities = default; }
            if (_asyncElementCounts.IsCreated) { _asyncElementCounts.Dispose(); _asyncElementCounts = default; }
            if (_asyncByteOffsets.IsCreated) { _asyncByteOffsets.Dispose(); _asyncByteOffsets = default; }
            if (_asyncOutput.IsCreated) { _asyncOutput.Dispose(); _asyncOutput = default; }
        }

        public override int ComputeBodySize(in SnapshotBuffer buffer)
        {
            if (!buffer.IsVariableLength)
                throw new FormatException(FormatError.ValidationFailed,
                    "Buffer module requires variable-length buffer");

            int count = buffer.Count;
            long size = CommonSectionFormat.HeaderSize;
            size += (long)count * 4;
            size += (long)(count + 1) * 4;
            if (WritesEnabledBits(in buffer)) size += (count + 7) / 8;

            int total = 0;
            for (int i = 0; i < buffer.Count; i++) total += buffer.RecordLengths[i];
            size += (long)total * RecordStride;

            if (size > int.MaxValue)
                throw new FormatException(FormatError.ValidationFailed,
                    "buffer section body too large");
            return (int)size;
        }

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            if (!buffer.IsVariableLength)
                throw new FormatException(FormatError.ValidationFailed,
                    "Buffer module requires variable-length buffer");

            int count = buffer.Count;
            bool hasEnabled = HasEnabledBits && buffer.HasEnabledStates;
            int dataStride = RecordStride;
            int rawStride = RawRecordStride;
            var refs = EntityRefOffsets;

            SectionBodyHelper.WriteVariableHeader(ref writer, count, dataStride, hasEnabled);
            SectionBodyHelper.WriteEntityIndices(ref writer, in buffer, idOf);

            int totalElements = 0;
            using (var offsets = new NativeArray<uint>(count + 1, Allocator.TempJob))
            {
                uint* optr = (uint*)offsets.GetUnsafePtr();
                optr[0] = 0;
                for (int i = 0; i < count; i++)
                {
                    totalElements += buffer.RecordLengths[i];
                    optr[i + 1] = (uint)(totalElements * dataStride);
                }
                writer.WriteBytes((byte*)optr, (count + 1) * 4);
            }

            if (hasEnabled)
            {
                int byteCount = (count + 7) / 8;
                using var eb = new NativeArray<byte>(byteCount, Allocator.TempJob);
                byte* eptr = (byte*)eb.GetUnsafePtr();
                for (int b = 0; b < byteCount; b++) eptr[b] = 0;
                for (int i = 0; i < count; i++)
                    if (buffer.EnabledStates[i] != 0)
                        eptr[i / 8] |= (byte)(1 << (i % 8));
                if (byteCount > 0)
                    writer.WriteBytes(eptr, byteCount);
            }

            if (totalElements == 0) return;

            byte* valuesPtr = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();

            if (refs.Length == 0)
            {
                writer.WriteBytes(valuesPtr, totalElements * rawStride);
                return;
            }

            int elementIndex = 0;
            for (int i = 0; i < count; i++)
            {
                int elems = buffer.RecordLengths[i];
                for (int k = 0; k < elems; k++)
                {
                    byte* src = valuesPtr + elementIndex * rawStride;
                    EntityRefCodec.WriteRecordWithRefs(
                        ref writer, src, refs, rawStride, idOf);
                    elementIndex++;
                }
            }
        }

        public override unsafe void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            var v = CommonSectionReader.Read(sectionBody);
            int dataStride = RecordStride;
            int rawStride = RawRecordStride;
            var refs = EntityRefOffsets;

            if (v.Header.IsFixedLength)
                throw new FormatException(FormatError.SchemaMismatch,
                    "section is fixed-length but module expects variable-length");

            if ((int)v.Header.Stride != dataStride)
                throw new FormatException(FormatError.SchemaMismatch,
                    $"stride mismatch: section={v.Header.Stride}, expected={dataStride}");

            bool hasEnabled = v.Header.HasEnabledBits && HasEnabledBits;
            if (v.Header.HasEnabledBits && !HasEnabledBits)
                throw new FormatException(FormatError.SchemaMismatch,
                    "section has enabled bits but module does not support them");

            int count = (int)v.Header.Count;

            byte[] expanded = rawStride > 0 ? new byte[rawStride] : null;
            fixed (byte* eptr = expanded)
            {
                for (int i = 0; i < count; i++)
                {
                    uint pid = v.EntityIndex[i];
                    if (pid >= persistentToEntity.Length)
                        throw new FormatException(FormatError.ValidationFailed,
                            "persistent id out of range");

                    Entity target = persistentToEntity[(int)pid];
                    if (target == Entity.Null)
                    {
                        target = em.CreateEntity();
                        persistentToEntity[(int)pid] = target;
                    }

                    uint offsetStart = v.RecordOffset[i];
                    uint offsetEnd = v.RecordOffset[i + 1];
                    int elementCount = dataStride > 0
                        ? (int)((offsetEnd - offsetStart) / (uint)dataStride)
                        : 0;

                    DynamicBuffer<T> buf;
                    if (em.HasComponent<T>(target))
                    {
                        buf = em.GetBuffer<T>(target, isReadOnly: false);
                        buf.Clear();
                    }
                    else
                    {
                        buf = em.AddBuffer<T>(target);
                    }

                    byte* src = v.Records + offsetStart;

                    if (dataStride == 0)
                    {
                        for (int k = 0; k < elementCount; k++)
                            buf.Add(default);
                    }
                    else if (refs.Length == 0)
                    {
                        for (int k = 0; k < elementCount; k++)
                        {
                            T value = default;
                            byte* valPtr = (byte*)&value;
                            byte* srcPtr = src + k * dataStride;
                            for (int b = 0; b < dataStride; b++)
                                valPtr[b] = srcPtr[b];
                            buf.Add(value);
                        }
                    }
                    else
                    {
                        for (int k = 0; k < elementCount; k++)
                        {
                            EntityRefCodec.ExpandRecord(
                                src + k * dataStride, dataStride, refs, rawStride,
                                persistentToEntity, eptr);

                            T value = default;
                            byte* valPtr = (byte*)&value;
                            for (int b = 0; b < rawStride; b++)
                                valPtr[b] = eptr[b];
                            buf.Add(value);
                        }
                    }

                    if (hasEnabled)
                        ApplyEnabled(target, em, SectionBodyHelper.UnpackEnabled(v.EnabledBits, i));
                }
            }
        }

        public override ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator)
            => RollbackHelper.BackupBuffer<T>(
                this, updateTargets, em, allocator, HasEnabledBits, ReadEnabled);

        public override void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em)
            => RollbackHelper.RestoreBuffer<T>(
                updateTargets, in backup, em, HasEnabledBits, WriteEnabled);
    }

    public abstract class SaveTypeModuleBufferEnableable<T> : SaveTypeModuleBuffer<T>
        where T : unmanaged, IBufferElementData, IEnableableComponent
    {
        public override bool HasEnabledBits => true;

        protected override void CaptureImpl(EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            if (HasEnabledBits)
                SnapshotCapture.CaptureChunksBufferEnableable<T>(query, em, ref buffer);
            else
                SnapshotCapture.CaptureChunksBuffer<T>(query, em, ref buffer);
        }

        protected override void AppendEnabledStates(EntityManager em, ref SnapshotBuffer buffer)
        {
            if (!HasEnabledBits) return;
            if (!buffer.HasEnabledStates) return;

            var entities = AsyncEntities;
            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (e == Entity.Null) { buffer.AppendEnabled(true); continue; }
                if (!em.Exists(e)) { buffer.AppendEnabled(true); continue; }
                buffer.AppendEnabled(em.IsComponentEnabled<T>(e));
            }
        }

        protected override bool ReadEnabled(Entity entity, EntityManager em)
            => em.IsComponentEnabled<T>(entity);

        protected override void WriteEnabled(Entity entity, EntityManager em, bool enabled)
            => em.SetComponentEnabled<T>(entity, enabled);
    }

    public abstract class SaveTypeModuleShared<T> : SaveTypeModuleFixed<T>
        where T : unmanaged, ISharedComponentData
    {
        public override SectionKind SectionKind => SectionKind.Shared;

        protected override ComponentType QueryType => ComponentType.ReadWrite<T>();
        protected override EntityQueryOptions QueryOptions => EntityQueryOptions.IncludeDisabledEntities;

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
                updateTargets, em, allocator,
                out var values, out var presence);

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

    public abstract class SaveTypeModuleChunk<T> : SaveTypeModule, SaveTypeModuleChunkMarker
    where T : unmanaged, IComponentData
    {
        public override int RawRecordStride => UnsafeUtility.SizeOf<T>();

        // B-5: Chunk も Component/Buffer/Shared と同じ EntityRef パイプラインを通る。
        // EntityRefOffsets は基底 SaveTypeModule で abstract 宣言されているため、
        // 具象サブクラス（Source Generator 生成含む）が必ず override する。
        // これにより RecordStride はデータレイアウト上の正しい stride を返す。
        //
        // 参考: SaveTypeModuleBase<T> は EmptyInts を override しているが、
        // 本クラスは SaveTypeModule から直接派生するため、その恩恵は受けない。
        // 「Entity を持たない Chunk 型」を登録する場合は、生成コード側で
        // s_entityRefOffsets = Array.Empty<int>() を emit すること。

        public override int RecordStride
            => UnsafeUtility.SizeOf<T>() - 4 * EntityRefOffsets.Length;

        public override bool HasEnabledBits => false;
        public override SectionKind SectionKind => SectionKind.ChunkComponent;
        public override bool ContributesToIdOf => false;

        public override EntityQuery BuildQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<T>() },
            });

        public override unsafe void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            buffer.InitChunkMemberStorage(Allocator.TempJob);

            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            var entityHandle = em.GetEntityTypeHandle();

            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var entities = chunk.GetNativeArray(entityHandle);
                int count = entities.Length;
                if (count == 0) continue;

                T value = em.GetChunkComponentData<T>(entities[0]);
                byte* src = (byte*)&value;
                byte* members = (byte*)entities.GetUnsafeReadOnlyPtr();
                buffer.AppendChunk(src, (Entity*)members, count);
            }
        }

        public override unsafe void FilterAfterIdOf(
            ref SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            if (!buffer.HasChunkMembers) return;

            var newValues = new NativeList<byte>(buffer.Values.Length, Allocator.TempJob);
            var newEntities = new NativeList<Entity>(buffer.Entities.Length, Allocator.TempJob);
            var newMembers = new NativeList<Entity>(buffer.ChunkMemberEntities.Length, Allocator.TempJob);
            var newOffsets = new NativeList<int>(buffer.ChunkMemberOffsets.Length, Allocator.TempJob);
            newOffsets.Add(0);

            int stride = buffer.Stride;
            byte* vptr = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();

            for (int c = 0; c < buffer.ChunkMemberOffsets.Length - 1; c++)
            {
                int start = buffer.ChunkMemberOffsets[c];
                int end = buffer.ChunkMemberOffsets[c + 1];

                int memberCount = 0;
                for (int i = start; i < end; i++)
                    if (idOf.ContainsKey(buffer.ChunkMemberEntities[i]))
                        memberCount++;

                if (memberCount == 0) continue;

                byte* src = vptr + c * stride;
                for (int b = 0; b < stride; b++)
                    newValues.Add(src[b]);

                newEntities.Add(buffer.Entities[c]);

                for (int i = start; i < end; i++)
                {
                    var e = buffer.ChunkMemberEntities[i];
                    if (idOf.ContainsKey(e))
                        newMembers.Add(e);
                }
                newOffsets.Add(newMembers.Length);
            }

            buffer.Values.Dispose();
            buffer.Entities.Dispose();
            buffer.ChunkMemberEntities.Dispose();
            buffer.ChunkMemberOffsets.Dispose();

            buffer.Values = newValues;
            buffer.Entities = newEntities;
            buffer.ChunkMemberEntities = newMembers;
            buffer.ChunkMemberOffsets = newOffsets;
        }

        public override int ComputeBodySize(in SnapshotBuffer buffer)
        {
            int groupCount = buffer.Entities.Length;
            int memberCount = buffer.ChunkMemberEntities.IsCreated
                ? buffer.ChunkMemberEntities.Length
                : 0;
            return ChunkComponentSectionWriter.ComputeFixedBodySize(
                groupCount, memberCount, RecordStride);
        }

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int groupCount = buffer.Entities.Length;
            int dataStride = RecordStride;
            int rawStride = RawRecordStride;
            var refs = EntityRefOffsets;

            using var memberOffset = new NativeArray<uint>(groupCount + 1, Allocator.TempJob);
            using var memberEntityIndex = new NativeArray<uint>(
                buffer.ChunkMemberEntities.Length, Allocator.TempJob);

            uint* mo = (uint*)memberOffset.GetUnsafePtr();
            mo[0] = 0;
            for (int i = 0; i < groupCount; i++)
                mo[i + 1] = (uint)buffer.ChunkMemberOffsets[i + 1];

            uint* mei = (uint*)memberEntityIndex.GetUnsafePtr();
            for (int i = 0; i < buffer.ChunkMemberEntities.Length; i++)
                mei[i] = (uint)idOf[buffer.ChunkMemberEntities[i]];

            ChunkComponentSectionWriter.WriteFixedHeader(
                ref writer, (uint)groupCount, (uint)dataStride, mo,
                buffer.ChunkMemberEntities.Length, mei);

            if (dataStride <= 0 || groupCount == 0) return;

            byte* records = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();

            if (refs.Length == 0)
            {
                writer.WriteBytes(records, groupCount * dataStride);
            }
            else
            {
                for (int g = 0; g < groupCount; g++)
                {
                    EntityRefCodec.WriteRecordWithRefs(
                        ref writer, records + g * rawStride, refs, rawStride, idOf);
                }
            }
        }

        public override unsafe void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            var v = ChunkComponentSectionReader.Read(sectionBody);
            int groupCount = (int)v.Header.GroupCount;
            if (groupCount == 0) return;

            for (int g = 0; g < groupCount; g++)
            {
                int start = (int)v.MemberOffset[g];
                int end = (int)v.MemberOffset[g + 1];
                if (start >= end) continue;

                uint pid = v.MemberEntityIndex[start];
                if (pid >= persistentToEntity.Length) continue;

                Entity target = persistentToEntity[(int)pid];
                if (target == Entity.Null) continue;
                if (!em.Exists(target)) continue;

                if (!em.HasChunkComponent<T>(target))
                    em.AddChunkComponentData<T>(target);
            }
        }

        public override void RemoveFromEntity(Entity entity, EntityManager em)
        {
        }

        public override unsafe ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator)
        {
            int n = updateTargets.Length;
            int stride = UnsafeUtility.SizeOf<T>();

            var values = new NativeArray<byte>(n * stride, allocator);
            var presence = new NativeArray<byte>(n, allocator);
            byte* vptr = (byte*)values.GetUnsafePtr();

            var updateSet = new NativeParallelHashSet<Entity>(
                n > 0 ? n : 16, Allocator.TempJob);

            var anchorList = new NativeList<Entity>(16, Allocator.TempJob);
            var anchorValueList = new NativeList<byte>(16 * stride, Allocator.TempJob);

            try
            {
                for (int i = 0; i < n; i++)
                    if (updateTargets[i] != Entity.Null)
                        updateSet.Add(updateTargets[i]);

                for (int i = 0; i < n; i++)
                {
                    var e = updateTargets[i];
                    if (e == Entity.Null || !em.Exists(e) || !em.HasChunkComponent<T>(e))
                    {
                        presence[i] = 0;
                        continue;
                    }

                    presence[i] = 1;
                    T v = em.GetChunkComponentData<T>(e);
                    byte* src = (byte*)&v;
                    byte* dst = vptr + i * stride;
                    for (int b = 0; b < stride; b++) dst[b] = src[b];
                }

                using (var chunks = BuildQuery(em).ToArchetypeChunkArray(Allocator.TempJob))
                {
                    var entityHandle = em.GetEntityTypeHandle();

                    for (int c = 0; c < chunks.Length; c++)
                    {
                        var entities = chunks[c].GetNativeArray(entityHandle);

                        Entity anchor = Entity.Null;
                        for (int i = 0; i < entities.Length; i++)
                        {
                            if (!updateSet.Contains(entities[i]))
                            {
                                anchor = entities[i];
                                break;
                            }
                        }

                        if (anchor == Entity.Null) continue;

                        anchorList.Add(anchor);
                        int start = anchorValueList.Length;
                        anchorValueList.ResizeUninitialized(start + stride);
                        T av = em.GetChunkComponentData<T>(anchor);
                        ModuleBytes.Write(av, (byte*)anchorValueList.GetUnsafePtr() + start);
                    }
                }

                return new ModuleBackup
                {
                    Module = this,
                    Values = values,
                    Presence = presence,
                    Enabled = default,
                    Count = n,
                    AnchorEntities = anchorList.ToArray(allocator),
                    AnchorValues = anchorValueList.ToArray(allocator),
                    AnchorCount = anchorList.Length,
                };
            }
            finally
            {
                if (updateSet.IsCreated) updateSet.Dispose();
                if (anchorList.IsCreated) anchorList.Dispose();
                if (anchorValueList.IsCreated) anchorValueList.Dispose();
            }
        }

        public override unsafe void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em)
        {
            int stride = UnsafeUtility.SizeOf<T>();
            byte* vptr = backup.Values.IsCreated
                ? (byte*)backup.Values.GetUnsafeReadOnlyPtr()
                : null;

            for (int i = 0; i < backup.Count; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null || !em.Exists(e)) continue;

                if (backup.Presence[i] == 0)
                {
                    if (em.HasChunkComponent<T>(e))
                        em.RemoveChunkComponent<T>(e);
                    continue;
                }

                if (!em.HasChunkComponent<T>(e))
                    em.AddChunkComponentData<T>(e);

                var chunk = em.GetChunk(e);
                em.SetChunkComponentData<T>(chunk, ModuleBytes.Read<T>(vptr + i * stride, stride));
            }

            if (backup.AnchorCount == 0 || !backup.AnchorValues.IsCreated) return;

            byte* aptr = (byte*)backup.AnchorValues.GetUnsafeReadOnlyPtr();
            for (int i = 0; i < backup.AnchorCount; i++)
            {
                var anchor = backup.AnchorEntities[i];
                if (anchor == Entity.Null || !em.Exists(anchor)) continue;
                if (!em.HasChunkComponent<T>(anchor)) continue;

                var chunk = em.GetChunk(anchor);
                em.SetChunkComponentData<T>(chunk, ModuleBytes.Read<T>(aptr + i * stride, stride));
            }
        }

        public unsafe SaveLoadErrorCode RestoreChunks(
            in ChunkComponentSectionView view,
            int groupCount,
            int stride,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            ref bool merged)
        {
            int memberCount = (int)view.MemberOffset[groupCount];

            var entityToPid = new NativeParallelHashMap<Entity, int>(
                memberCount > 0 ? memberCount : 16, Allocator.TempJob);

            try
            {
                for (int i = 0; i < persistentToEntity.Length; i++)
                {
                    var e = persistentToEntity[i];
                    if (e != Entity.Null) entityToPid.TryAdd(e, i);
                }

                var entityIndexToGroup = new NativeArray<int>(
                    persistentToEntity.Length, Allocator.TempJob);

                try
                {
                    for (int i = 0; i < entityIndexToGroup.Length; i++)
                        entityIndexToGroup[i] = -1;

                    for (int g = 0; g < groupCount; g++)
                    {
                        int start = (int)view.MemberOffset[g];
                        int end = (int)view.MemberOffset[g + 1];
                        for (int k = start; k < end; k++)
                        {
                            uint pid = view.MemberEntityIndex[k];
                            if (pid < (uint)entityIndexToGroup.Length)
                                entityIndexToGroup[(int)pid] = g;
                        }
                    }

                    return ApplyChunks(
                        view, groupCount, stride,
                        entityIndexToGroup, entityToPid,
                        persistentToEntity, em, ref merged);
                }
                finally
                {
                    if (entityIndexToGroup.IsCreated) entityIndexToGroup.Dispose();
                }
            }
            finally
            {
                if (entityToPid.IsCreated) entityToPid.Dispose();
            }
        }

        private unsafe SaveLoadErrorCode ApplyChunks(
            in ChunkComponentSectionView view,
            int groupCount,
            int stride,
            NativeArray<int> entityIndexToGroup,
            NativeParallelHashMap<Entity, int> entityToPid,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            ref bool merged)
        {
            var query = BuildQuery(em);
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            var entityHandle = em.GetEntityTypeHandle();

            int maxCapacity = 128;
            for (int c = 0; c < chunks.Length; c++)
            {
                int cap = chunks[c].Capacity;
                if (cap > maxCapacity) maxCapacity = cap;
            }

            var scratchGroups = new NativeArray<int>(maxCapacity, Allocator.TempJob);

            var refs = EntityRefOffsets;
            int rawStride = RawRecordStride;
            byte[] expanded = refs.Length > 0 ? new byte[rawStride] : null;

            try
            {
                for (int c = 0; c < chunks.Length; c++)
                {
                    var chunk = chunks[c];
                    var entities = chunk.GetNativeArray(entityHandle);
                    int count = entities.Length;
                    if (count == 0) continue;

                    int groupTotal = 0;

                    for (int i = 0; i < count; i++)
                    {
                        var e = entities[i];
                        if (!entityToPid.TryGetValue(e, out int pid)) continue;

                        int g = entityIndexToGroup[pid];
                        if (g < 0) continue;

                        scratchGroups[groupTotal++] = g;
                    }

                    if (groupTotal == 0) continue;

                    int representative = SelectRepresentative(scratchGroups, groupTotal);
                    if (representative < 0 || representative * stride + stride > view.RecordsLength)
                        continue;

                    byte* src = view.Records + representative * stride;

                    if (refs.Length == 0)
                    {
                        em.SetChunkComponentData<T>(
                            chunk,
                            ModuleBytes.Read<T>(src, rawStride));
                    }
                    else
                    {
                        fixed (byte* eptr = expanded)
                        {
                            EntityRefCodec.ExpandRecord(
                                src, stride, refs, rawStride,
                                persistentToEntity, eptr);
                            em.SetChunkComponentData<T>(
                                chunk,
                                ModuleBytes.Read<T>(eptr, rawStride));
                        }
                    }

                    int distinctGroups = CountDistinct(scratchGroups, groupTotal);
                    bool hasNonMember = groupTotal < count;

                    if (distinctGroups >= 2 || (distinctGroups == 1 && hasNonMember))
                        merged = true;
                }

                return SaveLoadErrorCode.None;
            }
            finally
            {
                if (scratchGroups.IsCreated) scratchGroups.Dispose();
            }
        }

        private static int CountDistinct(NativeArray<int> groups, int count)
        {
            if (count <= 1) return count;

            int distinct = 0;
            for (int i = 0; i < count; i++)
            {
                bool seen = false;
                for (int j = 0; j < i; j++)
                {
                    if (groups[j] == groups[i]) { seen = true; break; }
                }
                if (!seen) distinct++;
            }
            return distinct;
        }

        private static int SelectRepresentative(NativeArray<int> groups, int count)
        {
            groups.GetSubArray(0, count).Sort();

            int best = groups[0];
            int bestCount = 0;
            int runStart = 0;

            for (int i = 1; i <= count; i++)
            {
                if (i < count && groups[i] == groups[runStart]) continue;

                int runLength = i - runStart;
                if (runLength > bestCount)
                {
                    best = groups[runStart];
                    bestCount = runLength;
                }
                runStart = i;
            }

            return best;
        }
    }
}
