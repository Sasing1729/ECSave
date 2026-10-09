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
        public abstract ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator);
        public abstract void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em);
        public abstract void RemoveFromEntity(Entity entity, EntityManager em);
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
            => CommonSectionWriter.ComputeFixedBodySize(
                buffer.Count, RecordStride,
                hasEnabledBits: HasEnabledBits && buffer.HasEnabledStates);

        protected virtual void ApplyEnabled(Entity entity, EntityManager em, bool enabled) { }
        protected virtual bool ReadEnabled(Entity entity, EntityManager em) => true;
        protected virtual void WriteEnabled(Entity entity, EntityManager em, bool enabled) { }

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int count = buffer.Count;
            bool hasEnabled = HasEnabledBits && buffer.HasEnabledStates;
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
                        EntityRefCodec.ExpandRecord(
                            record, dataStride, refs, rawStride, persistentToEntity, eptr);
                        WriteValue(target, em, eptr);
                    }

                    if (hasEnabled)
                        ApplyEnabled(target, em, SectionBodyHelper.UnpackEnabled(v.EnabledBits, i));
                }
            }
        }

        public override ModuleBackup BackupForRollback(
            NativeArray<Entity> updateTargets, EntityManager em, Allocator allocator)
            => RollbackHelper.BackupFixed<T>(
                this, updateTargets, em, allocator, HasEnabledBits, ReadEnabled);

        public override void RestoreFromRollback(
            NativeArray<Entity> updateTargets, in ModuleBackup backup, EntityManager em)
            => RollbackHelper.RestoreFixed<T>(
                updateTargets, in backup, em, HasEnabledBits, WriteEnabled);

        protected abstract unsafe void WriteValue(Entity entity, EntityManager em, byte* value);

        public override void RemoveFromEntity(Entity entity, EntityManager em)
        {
            if (em.HasComponent<T>(entity))
                em.RemoveComponent<T>(entity);
        }
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
            => em.SetComponentEnabled<T>(entity, enabled);

        protected override bool ReadEnabled(Entity entity, EntityManager em)
            => em.IsComponentEnabled<T>(entity);

        protected override void WriteEnabled(Entity entity, EntityManager em, bool enabled)
            => em.SetComponentEnabled<T>(entity, enabled);
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
        protected virtual bool ReadEnabled(Entity entity, EntityManager em) => true;
        protected virtual void WriteEnabled(Entity entity, EntityManager em, bool enabled) { }

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

            SectionBodyHelper.WriteVariableHeader(ref writer, count, dataStride, hasEnabled);
            SectionBodyHelper.WriteEntityIndices(ref writer, in buffer, idOf);
            SectionBodyHelper.WriteRecordOffsets(ref writer, in buffer, count, dataStride);

            if (hasEnabled)
                SectionBodyHelper.WriteEnabledBits(ref writer, in buffer, count);

            int totalElements = 0;
            for (int i = 0; i < count; i++)
                totalElements += buffer.RecordLengths[i];
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
                    ApplyEnabled(target, em, SectionBodyHelper.UnpackEnabled(v.EnabledBits, i));
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

        public override void RemoveFromEntity(Entity entity, EntityManager em)
        {
            if (em.HasComponent<T>(entity))
                em.RemoveComponent<T>(entity);
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
            => em.SetComponentEnabled<T>(entity, enabled);

        protected override bool ReadEnabled(Entity entity, EntityManager em)
            => em.IsComponentEnabled<T>(entity);

        protected override void WriteEnabled(Entity entity, EntityManager em, bool enabled)
            => em.SetComponentEnabled<T>(entity, enabled);
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

            SectionBodyHelper.WriteFixedHeader(ref writer, count, RecordStride, hasEnabled: false);
            SectionBodyHelper.WriteEntityIndices(ref writer, in buffer, idOf);

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

        public override void RemoveFromEntity(Entity entity, EntityManager em)
        {
            if (em.HasComponent<T>(entity))
                em.RemoveComponent<T>(entity);
        }
    }
}