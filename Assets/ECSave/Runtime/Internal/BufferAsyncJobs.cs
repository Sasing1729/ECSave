using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// ScheduleCountJob / ScheduleCopyJob に渡す実行コンテキスト。
    /// 型固有の BufferTypeHandle は仮想メソッドの引数で別途渡す。
    /// </summary>
    public struct BufferAsyncContext
    {
        public EntityQuery Query;
        public EntityManager EntityManager;
        public NativeArray<int> ChunkStartIndices;
        public NativeArray<Entity> Entities;
        public NativeArray<int> ElementCounts;
        public NativeArray<int> ByteOffsets;
        public NativeArray<byte> Output;
        public int Stride;
    }

    // ジェネリックフォールバック（Burst 非対応、managed 実行）。
    // SourceGenerator が具象ジョブを生成しない場合に使われる。
    internal unsafe struct BufferCountJob<T> : IJobChunk
        where T : unmanaged, IBufferElementData
    {
        [ReadOnly] public BufferTypeHandle<T> BufferHandle;
        [ReadOnly] public EntityTypeHandle EntityHandle;
        [ReadOnly] public NativeArray<int> ChunkStartIndices;
        [NativeDisableParallelForRestriction] public NativeArray<Entity> Entities;
        [NativeDisableParallelForRestriction] public NativeArray<int> ElementCounts;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityHandle);
            var buffers = chunk.GetBufferAccessor(ref BufferHandle);
            int start = ChunkStartIndices[unfilteredChunkIndex];
            int len = entities.Length < buffers.Length ? entities.Length : buffers.Length;
            for (int i = 0; i < len; i++)
            {
                Entities[start + i] = entities[i];
                ElementCounts[start + i] = buffers[i].Length;
            }
        }
    }

    internal unsafe struct BufferCopyJob<T> : IJobChunk
        where T : unmanaged, IBufferElementData
    {
        [ReadOnly] public BufferTypeHandle<T> BufferHandle;
        [ReadOnly] public NativeArray<int> ChunkStartIndices;
        [ReadOnly] public NativeArray<int> ByteOffsets;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Output;
        public int Stride;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var buffers = chunk.GetBufferAccessor(ref BufferHandle);
            int start = ChunkStartIndices[unfilteredChunkIndex];
            byte* outPtr = (byte*)Output.GetUnsafePtr();

            for (int i = 0; i < buffers.Length; i++)
            {
                var buf = buffers[i];
                int len = buf.Length;
                if (len == 0) continue;

                byte* src = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(
                    buf.AsNativeArray());
                byte* dst = outPtr + ByteOffsets[start + i];
                UnsafeUtility.MemCpy(dst, src, (long)len * Stride);
            }
        }
    }
}
