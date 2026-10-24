using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    public struct SnapshotBuffer : IDisposable
    {
        public NativeList<Entity> Entities;
        public NativeList<byte> Values;
        public NativeList<byte> EnabledStates;
        public NativeList<int> RecordLengths;
        public NativeList<Entity> ChunkMemberEntities;
        public NativeList<int> ChunkMemberOffsets;
        public int Stride;
        public bool IsVariableLength;

        public SnapshotBuffer(int initialCapacity, int stride, Allocator allocator)
        {
            int cap = initialCapacity > 0 ? initialCapacity : 16;
            Entities = new NativeList<Entity>(cap, allocator);
            Values = new NativeList<byte>(cap * Math.Max(stride, 1), allocator);
            EnabledStates = default;
            RecordLengths = default;
            ChunkMemberEntities = default;
            ChunkMemberOffsets = default;
            Stride = stride;
            IsVariableLength = false;
        }

        public bool IsCreated => Entities.IsCreated;
        public int Count => Entities.IsCreated ? Entities.Length : 0;
        public bool HasEnabledStates => EnabledStates.IsCreated;
        public bool HasRecordLengths => RecordLengths.IsCreated;
        public bool HasChunkMembers => ChunkMemberEntities.IsCreated;

        public void InitEnabledStates(Allocator allocator)
        {
            if (!EnabledStates.IsCreated)
                EnabledStates = new NativeList<byte>(Entities.Capacity, allocator);
        }

        public void InitRecordLengths(Allocator allocator)
        {
            if (!RecordLengths.IsCreated)
                RecordLengths = new NativeList<int>(Entities.Capacity, allocator);
            IsVariableLength = true;
        }

        public void InitChunkMemberStorage(Allocator allocator)
        {
            if (!ChunkMemberEntities.IsCreated)
                ChunkMemberEntities = new NativeList<Entity>(256, allocator);
            if (!ChunkMemberOffsets.IsCreated)
            {
                ChunkMemberOffsets = new NativeList<int>(64, allocator);
                ChunkMemberOffsets.Add(0);
            }
        }

        public unsafe void BulkAppendPlain<T>(NativeArray<Entity> entities, NativeArray<T> values)
            where T : unmanaged
        {
            int count = entities.Length;
            if (count == 0) return;
            if (!entities.IsCreated || !values.IsCreated)
                throw new InvalidOperationException("Async arrays are not created");
            if (values.Length != count)
                throw new InvalidOperationException(
                    $"Async array length mismatch: entities={count}, values={values.Length}");
            if (IsVariableLength)
                throw new InvalidOperationException("BulkAppendPlain is not supported in variable-length mode");

            int stride = UnsafeUtility.SizeOf<T>();
            if (stride != Stride)
                throw new InvalidOperationException(
                    $"stride mismatch: T size={stride}, buffer stride={Stride}");

            int eStart = Entities.Length;
            Entities.ResizeUninitialized(eStart + count);
            UnsafeUtility.MemCpy(
                (byte*)Entities.GetUnsafePtr() + (long)eStart * sizeof(Entity),
                NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(entities),
                (long)count * sizeof(Entity));

            if (stride > 0)
            {
                int vStart = Values.Length;
                Values.ResizeUninitialized(vStart + count * stride);
                UnsafeUtility.MemCpy(
                    (byte*)Values.GetUnsafePtr() + vStart,
                    NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values),
                    (long)count * stride);
            }
        }

        // === S3a-3B 追加 ===
        public unsafe void BulkAppendVariable(
            NativeArray<Entity> entities,
            NativeArray<int> elementCounts,
            NativeArray<byte> values)
        {
            int count = entities.Length;
            if (count == 0) return;
            if (!entities.IsCreated || !elementCounts.IsCreated)
                throw new InvalidOperationException("Async arrays are not created");
            if (elementCounts.Length != count)
                throw new InvalidOperationException(
                    $"elementCounts.Length={elementCounts.Length} != entities.Length={count}");
            if (!IsVariableLength)
                throw new InvalidOperationException("BulkAppendVariable requires variable-length mode");

            int eStart = Entities.Length;
            Entities.ResizeUninitialized(eStart + count);
            UnsafeUtility.MemCpy(
                (byte*)Entities.GetUnsafePtr() + (long)eStart * sizeof(Entity),
                NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(entities),
                (long)count * sizeof(Entity));

            int rStart = RecordLengths.Length;
            RecordLengths.ResizeUninitialized(rStart + count);
            UnsafeUtility.MemCpy(
                (byte*)RecordLengths.GetUnsafePtr() + (long)rStart * sizeof(int),
                NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(elementCounts),
                (long)count * sizeof(int));

            if (values.IsCreated && values.Length > 0)
            {
                int vStart = Values.Length;
                Values.ResizeUninitialized(vStart + values.Length);
                UnsafeUtility.MemCpy(
                    (byte*)Values.GetUnsafePtr() + vStart,
                    NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values),
                    values.Length);
            }
        }

        public unsafe void Append(Entity entity, byte* value)
        {
            if (IsVariableLength)
                throw new InvalidOperationException("Use AppendVariable in variable-length mode");

            Entities.Add(entity);
            int start = Values.Length;
            Values.ResizeUninitialized(start + Stride);
            byte* dst = (byte*)Values.GetUnsafePtr() + start;
            for (int i = 0; i < Stride; i++) dst[i] = value[i];
        }

        public unsafe void AppendVariable(Entity entity, byte* value, int elementCount)
        {
            if (!IsVariableLength)
                throw new InvalidOperationException("Not in variable-length mode");

            Entities.Add(entity);
            RecordLengths.Add(elementCount);

            int bytes = elementCount * Stride;
            if (bytes > 0)
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));
                int start = Values.Length;
                Values.ResizeUninitialized(start + bytes);
                byte* dst = (byte*)Values.GetUnsafePtr() + start;
                for (int i = 0; i < bytes; i++) dst[i] = value[i];
            }
        }

        public unsafe void AppendChunk(byte* value, Entity* members, int memberCount)
        {
            int stride = Stride;
            int vs = Values.Length;
            Values.ResizeUninitialized(vs + stride);
            byte* vdst = (byte*)Values.GetUnsafePtr() + vs;
            for (int i = 0; i < stride; i++) vdst[i] = value[i];

            Entities.Add(members[0]);

            for (int i = 0; i < memberCount; i++)
                ChunkMemberEntities.Add(members[i]);
            ChunkMemberOffsets.Add(ChunkMemberEntities.Length);
        }

        public void AppendEnabled(bool enabled)
        {
            if (!EnabledStates.IsCreated)
                throw new InvalidOperationException("EnabledStates not initialized");
            EnabledStates.Add(enabled ? (byte)1 : (byte)0);
        }

        public unsafe byte* GetValuePtr(int index)
        {
            if (IsVariableLength)
            {
                int offset = 0;
                for (int i = 0; i < index; i++)
                    offset += RecordLengths[i] * Stride;
                return Stride == 0 ? null : (byte*)Values.GetUnsafeReadOnlyPtr() + offset;
            }
            return Stride == 0 ? null : (byte*)Values.GetUnsafeReadOnlyPtr() + index * Stride;
        }

        public void Dispose()
        {
            if (Entities.IsCreated) Entities.Dispose();
            if (Values.IsCreated) Values.Dispose();
            if (EnabledStates.IsCreated) EnabledStates.Dispose();
            if (RecordLengths.IsCreated) RecordLengths.Dispose();
            if (ChunkMemberEntities.IsCreated) ChunkMemberEntities.Dispose();
            if (ChunkMemberOffsets.IsCreated) ChunkMemberOffsets.Dispose();
            Entities = default;
            Values = default;
            EnabledStates = default;
            RecordLengths = default;
            ChunkMemberEntities = default;
            ChunkMemberOffsets = default;
        }
    }
}
