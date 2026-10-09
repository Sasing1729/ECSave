using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;

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
        public abstract EntityQuery BuildQuery(EntityManager em);
        public abstract void Capture(EntityQuery query, EntityManager em, ref SnapshotBuffer buffer);
        public abstract int ComputeBodySize(in SnapshotBuffer buffer);
        public abstract void WriteSectionBody(
            ref FormatWriter writer, in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf);
        public abstract void ApplySectionBody(
            ByteView sectionBody, NativeArray<Entity> persistentToEntity, EntityManager em);
    }

    public abstract class SaveTypeModule<T> : SaveTypeModule
        where T : unmanaged, IComponentData
    {
        private static readonly int[] s_noRefs = System.Array.Empty<int>();

        public override int RawRecordStride => UnsafeUtility.SizeOf<T>();
        public override int[] EntityRefOffsets => s_noRefs;
        public override int RecordStride => UnsafeUtility.SizeOf<T>() - 4 * EntityRefOffsets.Length;
        public override bool HasEnabledBits => false;

        public override EntityQuery BuildQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<T>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

        public override int ComputeBodySize(in SnapshotBuffer buffer)
        {
            int count = buffer.Count;
            bool hasEnabled = HasEnabledBits && buffer.HasEnabledStates;
            return CommonSectionWriter.ComputeFixedBodySize(
                count, RecordStride, hasEnabledBits: hasEnabled);
        }

        protected virtual void ApplyEnabled(Entity entity, EntityManager em, bool enabled) { }

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int count = buffer.Count;
            bool hasEnabled = HasEnabledBits && buffer.HasEnabledStates;
            var refs = EntityRefOffsets;
            int rawStride = RawRecordStride;
            int dataStride = RecordStride;

            uint flags = ECSaveFormatConstants.SectionFlags_FixedLength;
            if (hasEnabled) flags |= ECSaveFormatConstants.SectionFlags_HasEnabledBits;

            writer.WriteU32((uint)count);
            writer.WriteU32(flags);
            writer.WriteU32((uint)dataStride);

            if (count > 0)
            {
                using var indices = new NativeArray<uint>(count, Allocator.TempJob);
                uint* idst = (uint*)indices.GetUnsafePtr();
                for (int i = 0; i < count; i++)
                    idst[i] = (uint)idOf[buffer.Entities[i]];
                writer.WriteBytes((byte*)idst, count * 4);
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

            if (dataStride > 0 && count > 0)
            {
                if (refs.Length == 0)
                {
                    byte* src = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();
                    writer.WriteBytes(src, count * rawStride);
                }
                else
                {
                    for (int i = 0; i < count; i++)
                    {
                        byte* src = buffer.GetValuePtr(i);
                        WriteRecordWithRefs(ref writer, src, refs, rawStride, idOf);
                    }
                }
            }
        }

        private static unsafe void WriteRecordWithRefs(
            ref FormatWriter writer,
            byte* src,
            int[] refOffsets,
            int rawStride,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int srcOff = 0;
            for (int r = 0; r < refOffsets.Length; r++)
            {
                int refOff = refOffsets[r];
                int gap = refOff - srcOff;
                if (gap > 0)
                    writer.WriteBytes(src + srcOff, gap);

                Entity e = *(Entity*)(src + refOff);
                uint pid;
                if (e == Entity.Null)
                    pid = EntityRefConstants.NullRef;
                else if (idOf.TryGetValue(e, out int id))
                    pid = (uint)id;
                else
                    pid = EntityRefConstants.UnresolvedRef;

                writer.WriteU32(pid);
                srcOff = refOff + 8;
            }
            int remaining = rawStride - srcOff;
            if (remaining > 0)
                writer.WriteBytes(src + srcOff, remaining);
        }

        public override unsafe void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            var v = CommonSectionReader.Read(sectionBody);
            int count = (int)v.Header.Count;
            int dataStride = RecordStride;
            int rawStride = RawRecordStride;
            var refs = EntityRefOffsets;

            if ((int)v.Header.Stride != dataStride)
                throw new FormatException(FormatError.SchemaMismatch,
                    $"stride mismatch: section={v.Header.Stride}, expected={dataStride}");

            bool hasEnabled = v.Header.HasEnabledBits && HasEnabledBits;
            if (v.Header.HasEnabledBits && !HasEnabledBits)
                throw new FormatException(FormatError.SchemaMismatch,
                    "section has enabled bits but module does not support them");

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

                    byte* record = dataStride > 0 ? v.Records + i * dataStride : null;
                    if (refs.Length == 0)
                    {
                        WriteValue(target, em, record);
                    }
                    else
                    {
                        ExpandRecord(record, dataStride, refs, rawStride, persistentToEntity, eptr);
                        WriteValue(target, em, eptr);
                    }

                    if (hasEnabled)
                    {
                        bool enabled = (v.EnabledBits[i / 8] & (1 << (i % 8))) != 0;
                        ApplyEnabled(target, em, enabled);
                    }
                }
            }
        }

        private static unsafe void ExpandRecord(
            byte* src,
            int dataStride,
            int[] refOffsets,
            int rawStride,
            NativeArray<Entity> persistentToEntity,
            byte* dst)
        {
            int dstOff = 0;
            int srcOff = 0;
            for (int r = 0; r < refOffsets.Length; r++)
            {
                int refOff = refOffsets[r];
                int gap = refOff - dstOff;
                for (int b = 0; b < gap; b++)
                    dst[dstOff + b] = src[srcOff + b];
                dstOff += gap;
                srcOff += gap;

                uint pid = *(uint*)(src + srcOff);
                Entity e;
                if (pid == EntityRefConstants.NullRef)
                    e = Entity.Null;
                else if (pid == EntityRefConstants.UnresolvedRef)
                    e = Entity.Null;
                else if (pid < (uint)persistentToEntity.Length)
                    e = persistentToEntity[(int)pid];
                else
                    e = Entity.Null;

                *(Entity*)(dst + dstOff) = e;
                dstOff += 8;
                srcOff += 4;
            }
            int remaining = dataStride - srcOff;
            for (int b = 0; b < remaining; b++)
                dst[dstOff + b] = src[srcOff + b];
        }

        protected abstract unsafe void WriteValue(Entity entity, EntityManager em, byte* value);
    }

    public abstract class SaveTypeModulePlain<T> : SaveTypeModule<T>
        where T : unmanaged, IComponentData
    {
        public override void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            SnapshotCapture.CaptureChunksPlain<T>(query, em, ref buffer);
        }
    }

    public abstract class SaveTypeModuleEnableable<T> : SaveTypeModule<T>
        where T : unmanaged, IComponentData, IEnableableComponent
    {
        public override bool HasEnabledBits => true;

        public override void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            SnapshotCapture.CaptureChunksEnableable<T>(query, em, ref buffer);
        }

        protected override void ApplyEnabled(Entity entity, EntityManager em, bool enabled)
        {
            em.SetComponentEnabled<T>(entity, enabled);
        }
    }

    public abstract class SaveTypeModuleBuffer<T> : SaveTypeModule
        where T : unmanaged, IBufferElementData
    {
        private static readonly int[] s_noRefs = System.Array.Empty<int>();

        public override int RawRecordStride => UnsafeUtility.SizeOf<T>();
        public override int[] EntityRefOffsets => s_noRefs;
        public override int RecordStride => UnsafeUtility.SizeOf<T>() - 4 * EntityRefOffsets.Length;
        public override bool HasEnabledBits => false;

        protected virtual void ApplyEnabled(Entity entity, EntityManager em, bool enabled) { }

        public override EntityQuery BuildQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<T>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

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
        {
            SnapshotCapture.CaptureChunksBuffer<T>(query, em, ref buffer);
        }

        public override int ComputeBodySize(in SnapshotBuffer buffer)
        {
            if (!buffer.IsVariableLength)
                throw new FormatException(FormatError.ValidationFailed,
                    "Buffer module requires variable-length buffer");

            int count = buffer.Count;
            bool hasEnabled = HasEnabledBits && buffer.HasEnabledStates;

            long size = CommonSectionFormat.HeaderSize;
            size += (long)count * 4;
            size += (long)(count + 1) * 4;
            if (hasEnabled) size += (count + 7) / 8;
            int totalElements = 0;
            for (int i = 0; i < count; i++)
                totalElements += buffer.RecordLengths[i];
            size += (long)totalElements * RecordStride;

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

            uint flags = 0;
            if (hasEnabled) flags |= ECSaveFormatConstants.SectionFlags_HasEnabledBits;

            writer.WriteU32((uint)count);
            writer.WriteU32(flags);
            writer.WriteU32((uint)dataStride);

            if (count > 0)
            {
                using var indices = new NativeArray<uint>(count, Allocator.TempJob);
                uint* idst = (uint*)indices.GetUnsafePtr();
                for (int i = 0; i < count; i++)
                    idst[i] = (uint)idOf[buffer.Entities[i]];
                writer.WriteBytes((byte*)idst, count * 4);
            }

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

            int totalBytes = totalElements * dataStride;
            if (totalBytes > 0)
            {
                byte* src = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();
                writer.WriteBytes(src, totalBytes);
            }
        }

        public override unsafe void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            var v = CommonSectionReader.Read(sectionBody);
            int dataStride = RecordStride;

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

                if (dataStride > 0 && elementCount > 0)
                {
                    byte* src = v.Records + offsetStart;
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
                else if (dataStride == 0)
                {
                    for (int k = 0; k < elementCount; k++)
                        buf.Add(default);
                }

                if (hasEnabled)
                {
                    bool enabled = (v.EnabledBits[i / 8] & (1 << (i % 8))) != 0;
                    ApplyEnabled(target, em, enabled);
                }
            }
        }
    }

    public abstract class SaveTypeModuleBufferEnableable<T> : SaveTypeModuleBuffer<T>
        where T : unmanaged, IBufferElementData, IEnableableComponent
    {
        public override bool HasEnabledBits => true;

        protected override void CaptureImpl(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            SnapshotCapture.CaptureChunksBufferEnableable<T>(query, em, ref buffer);
        }

        protected override void ApplyEnabled(Entity entity, EntityManager em, bool enabled)
        {
            em.SetComponentEnabled<T>(entity, enabled);
        }
    }

    public abstract class SaveTypeModuleShared<T> : SaveTypeModule
        where T : unmanaged, ISharedComponentData
    {
        private static readonly int[] s_noRefs = System.Array.Empty<int>();

        public override int RawRecordStride => UnsafeUtility.SizeOf<T>();
        public override int[] EntityRefOffsets => s_noRefs;
        public override int RecordStride => UnsafeUtility.SizeOf<T>();
        public override bool HasEnabledBits => false;
        public override SectionKind SectionKind => SectionKind.Shared;

        public override EntityQuery BuildQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<T>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });

        public override void Capture(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
        {
            SnapshotCapture.CaptureChunksShared<T>(query, em, ref buffer);
        }

        public override int ComputeBodySize(in SnapshotBuffer buffer)
            => CommonSectionWriter.ComputeFixedBodySize(
                buffer.Count, RecordStride, hasEnabledBits: false);

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int count = buffer.Count;

            writer.WriteU32((uint)count);
            writer.WriteU32(ECSaveFormatConstants.SectionFlags_FixedLength);
            writer.WriteU32((uint)RecordStride);

            if (count > 0)
            {
                using var indices = new NativeArray<uint>(count, Allocator.TempJob);
                uint* idst = (uint*)indices.GetUnsafePtr();
                for (int i = 0; i < count; i++)
                    idst[i] = (uint)idOf[buffer.Entities[i]];
                writer.WriteBytes((byte*)idst, count * 4);
            }

            if (RecordStride > 0 && count > 0)
            {
                byte* src = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();
                writer.WriteBytes(src, count * RecordStride);
            }
        }

        public override unsafe void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            var v = CommonSectionReader.Read(sectionBody);
            int count = (int)v.Header.Count;
            if ((int)v.Header.Stride != RecordStride)
                throw new FormatException(FormatError.SchemaMismatch,
                    $"stride mismatch: section={v.Header.Stride}, expected={RecordStride}");

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

                T value = default;
                byte* valPtr = (byte*)&value;
                byte* srcPtr = v.Records + i * RecordStride;
                for (int b = 0; b < RecordStride; b++)
                    valPtr[b] = srcPtr[b];

                if (em.HasComponent<T>(target))
                    em.SetSharedComponent(target, value);
                else
                    em.AddSharedComponent(target, value);
            }
        }
    }
}
