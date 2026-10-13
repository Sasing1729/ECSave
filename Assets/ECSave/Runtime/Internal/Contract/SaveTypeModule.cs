using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;

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

        public virtual bool ContributesToIdOf => true;

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
        public override void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            => SnapshotCapture.CaptureChunksPlain<T>(query, em, ref buffer);
    }

    public abstract class SaveTypeModuleEnableable<T> : SaveTypeModule<T>
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        public override bool HasEnabledBits => true;

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

        private static void RequireVariableLength(in SnapshotBuffer buffer)
        {
            if (!buffer.IsVariableLength)
                throw new FormatException(FormatError.ValidationFailed,
                    "Buffer module requires variable-length buffer");
        }

        private static int TotalElements(in SnapshotBuffer buffer)
        {
            int total = 0;
            for (int i = 0; i < buffer.Count; i++)
                total += buffer.RecordLengths[i];
            return total;
        }

        public override int ComputeBodySize(in SnapshotBuffer buffer)
        {
            RequireVariableLength(in buffer);

            int count = buffer.Count;
            long size = CommonSectionFormat.HeaderSize;
            size += (long)count * 4;
            size += (long)(count + 1) * 4;
            if (WritesEnabledBits(in buffer)) size += (count + 7) / 8;
            size += (long)TotalElements(in buffer) * RecordStride;

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

        private unsafe void WriteBufferElements(
            Entity target, EntityManager em, byte* src, int elementCount)
        {
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

            int dataStride = RecordStride;
            for (int k = 0; k < elementCount; k++)
                buf.Add(dataStride > 0
                    ? ModuleBytes.Read<T>(src + k * dataStride, dataStride)
                    : default);
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
        public override int[] EntityRefOffsets => ModuleShared.EmptyInts;
        public override int RecordStride => UnsafeUtility.SizeOf<T>();
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
            int stride = RecordStride;

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

            byte* records = stride > 0 && groupCount > 0
                ? (byte*)buffer.Values.GetUnsafeReadOnlyPtr()
                : null;

            ChunkComponentSectionWriter.WriteFixed(
                ref writer, (uint)groupCount, (uint)stride, mo,
                buffer.ChunkMemberEntities.Length, mei, records);
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

        // ── Anchor-based rollback ────────────────────

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

        // ── P6: restore chunks (with merged detection) ────

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
                        entityIndexToGroup, entityToPid, em, ref merged);
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

                    em.SetChunkComponentData<T>(
                        chunk,
                        ModuleBytes.Read<T>(view.Records + representative * stride, stride));

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
