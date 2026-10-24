using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using FormatError = ECSave.Format.FormatError;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal
{
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
            if (HasEnabledBits) buffer.InitEnabledStates(Allocator.TempJob);
            CaptureImpl(query, em, ref buffer);
        }

        protected virtual void CaptureImpl(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            => SnapshotCapture.CaptureChunksBuffer<T>(query, em, ref buffer);

        protected virtual JobHandle ScheduleCountJob(
            BufferTypeHandle<T> bufferHandle, BufferAsyncContext ctx, JobHandle deps)
            => new BufferCountJob<T>
            {
                BufferHandle = bufferHandle,
                EntityHandle = ctx.EntityManager.GetEntityTypeHandle(),
                ChunkStartIndices = ctx.ChunkStartIndices,
                Entities = ctx.Entities,
                ElementCounts = ctx.ElementCounts,
            }.ScheduleParallel(ctx.Query, deps);

        protected virtual JobHandle ScheduleCopyJob(
            BufferTypeHandle<T> bufferHandle, BufferAsyncContext ctx, JobHandle deps)
            => new BufferCopyJob<T>
            {
                BufferHandle = bufferHandle,
                ChunkStartIndices = ctx.ChunkStartIndices,
                ElementCounts = ctx.ElementCounts,
                ByteOffsets = ctx.ByteOffsets,
                Output = ctx.Output,
                Stride = ctx.Stride,
            }.ScheduleParallel(ctx.Query, deps);

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

            if (totalEntities == 0) return dependsOn;

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
                    ScheduleCopyJob(bufferHandle, ctx, default).Complete();
                }

                buffer.BulkAppendVariable(_asyncEntities, _asyncElementCounts, _asyncOutput);
                AppendEnabledStates(em, ref buffer);
            }
            finally { CancelAsyncCapture(); }
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
                throw new FormatException(FormatError.ValidationFailed, "buffer section body too large");
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
                    if (buffer.EnabledStates[i] != 0) eptr[i / 8] |= (byte)(1 << (i % 8));
                if (byteCount > 0) writer.WriteBytes(eptr, byteCount);
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
                    EntityRefCodec.WriteRecordWithRefs(ref writer, src, refs, rawStride, idOf);
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
                        ? (int)((offsetEnd - offsetStart) / (uint)dataStride) : 0;

                    DynamicBuffer<T> buf;
                    if (em.HasComponent<T>(target))
                    {
                        buf = em.GetBuffer<T>(target, isReadOnly: false);
                        buf.Clear();
                    }
                    else buf = em.AddBuffer<T>(target);

                    byte* src = v.Records + offsetStart;

                    if (dataStride == 0)
                    {
                        for (int k = 0; k < elementCount; k++) buf.Add(default);
                    }
                    else if (refs.Length == 0)
                    {
                        for (int k = 0; k < elementCount; k++)
                        {
                            T value = default;
                            byte* valPtr = (byte*)&value;
                            byte* srcPtr = src + k * dataStride;
                            for (int b = 0; b < dataStride; b++) valPtr[b] = srcPtr[b];
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
                            for (int b = 0; b < rawStride; b++) valPtr[b] = eptr[b];
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
}
