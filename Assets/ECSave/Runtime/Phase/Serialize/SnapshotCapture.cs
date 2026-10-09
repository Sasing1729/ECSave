using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// W1 の Capture を担う（7.2節、ADR-09、A-22、A-29）。
    /// </summary>
    public static unsafe class SnapshotCapture
    {
        // ── IComponentData ───────────────────────
        public static void CaptureChunksPlain<T>(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            where T : unmanaged, IComponentData
        {
            int stride = UnsafeUtility.SizeOf<T>();
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            var valueHandle = em.GetComponentTypeHandle<T>(isReadOnly: true);
            var entityHandle = em.GetEntityTypeHandle();

            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var values = chunk.GetNativeArray(ref valueHandle);
                var entities = chunk.GetNativeArray(entityHandle);
                int count = entities.Length;
                if (count == 0) continue;

                byte* src = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values);
                for (int i = 0; i < count; i++)
                    buffer.Append(entities[i], src + i * stride);
            }
        }

        public static void CaptureChunksEnableable<T>(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            where T : unmanaged, IComponentData, IEnableableComponent
        {
            int stride = UnsafeUtility.SizeOf<T>();
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            var valueHandle = em.GetComponentTypeHandle<T>(isReadOnly: true);
            var entityHandle = em.GetEntityTypeHandle();

            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var values = chunk.GetNativeArray(ref valueHandle);
                var entities = chunk.GetNativeArray(entityHandle);
                int count = entities.Length;
                if (count == 0) continue;

                byte* src = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(values);
                for (int i = 0; i < count; i++)
                {
                    var e = entities[i];
                    buffer.Append(e, src + i * stride);
                    buffer.AppendEnabled(em.IsComponentEnabled<T>(e));
                }
            }
        }

        // ── DynamicBuffer（A-29 検証済み）────────
        public static void CaptureChunksBuffer<T>(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            where T : unmanaged, IBufferElementData
        {
            int stride = UnsafeUtility.SizeOf<T>();
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            var bufferHandle = em.GetBufferTypeHandle<T>(isReadOnly: true);
            var entityHandle = em.GetEntityTypeHandle();

            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var accessor = chunk.GetBufferAccessor(ref bufferHandle);
                var entities = chunk.GetNativeArray(entityHandle);
                int count = entities.Length;
                if (count == 0) continue;

                for (int i = 0; i < count; i++)
                {
                    var dynamicBuffer = accessor[i];
                    int elementCount = dynamicBuffer.Length;
                    byte* src = elementCount > 0
                        ? (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(dynamicBuffer.AsNativeArray())
                        : null;

                    buffer.AppendVariable(entities[i], src, elementCount);
                }
            }
        }

        /// <summary>
        /// Enableable な DynamicBuffer の Capture（Phase 5c-3）。
        /// A-05 で検証済みの EntityManager.IsComponentEnabled を使う。
        /// </summary>
        public static void CaptureChunksBufferEnableable<T>(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            where T : unmanaged, IBufferElementData, IEnableableComponent
        {
            int stride = UnsafeUtility.SizeOf<T>();
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
            var bufferHandle = em.GetBufferTypeHandle<T>(isReadOnly: true);
            var entityHandle = em.GetEntityTypeHandle();

            for (int c = 0; c < chunks.Length; c++)
            {
                var chunk = chunks[c];
                var accessor = chunk.GetBufferAccessor(ref bufferHandle);
                var entities = chunk.GetNativeArray(entityHandle);
                int count = entities.Length;
                if (count == 0) continue;

                for (int i = 0; i < count; i++)
                {
                    var dynamicBuffer = accessor[i];
                    int elementCount = dynamicBuffer.Length;
                    byte* src = elementCount > 0
                        ? (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(dynamicBuffer.AsNativeArray())
                        : null;

                    var e = entities[i];
                    buffer.AppendVariable(e, src, elementCount);
                    buffer.AppendEnabled(em.IsComponentEnabled<T>(e));
                }
            }
        }

        public static void CaptureChunksShared<T>(
            EntityQuery query, EntityManager em, ref SnapshotBuffer buffer)
            where T : unmanaged, ISharedComponentData
        {
            int stride = UnsafeUtility.SizeOf<T>();

            using var entities = query.ToEntityArray(Allocator.TempJob);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var value = em.GetSharedComponent<T>(e);
                byte* src = (byte*)UnsafeUtility.AddressOf(ref value);
                buffer.Append(e, src);
            }
        }
    }
}
