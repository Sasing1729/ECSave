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
        public NativeList<int> RecordLengths;  // 可変長時のみ。Length == Entities.Length
        public int Stride;
        public bool IsVariableLength;

        public SnapshotBuffer(int initialCapacity, int stride, Allocator allocator)
        {
            int cap = initialCapacity > 0 ? initialCapacity : 16;
            Entities = new NativeList<Entity>(cap, allocator);
            Values = new NativeList<byte>(cap * Math.Max(stride, 1), allocator);
            EnabledStates = default;
            RecordLengths = default;
            Stride = stride;
            IsVariableLength = false;
        }

        public bool IsCreated => Entities.IsCreated;
        public int Count => Entities.IsCreated ? Entities.Length : 0;
        public bool HasEnabledStates => EnabledStates.IsCreated;
        public bool HasRecordLengths => RecordLengths.IsCreated;

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

        /// <summary>固定長モードで 1 レコード追加する。</summary>
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

        /// <summary>
        /// 可変長モードで 1 レコード（elementCount 個の要素）を追加する。
        /// value は elementCount × Stride バイト。空 Buffer は elementCount=0、value=null 可。
        /// </summary>
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
            Entities = default;
            Values = default;
            EnabledStates = default;
            RecordLengths = default;
        }
    }
}
