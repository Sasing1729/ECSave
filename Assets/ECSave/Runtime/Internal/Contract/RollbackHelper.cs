using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class RollbackHelper
    {
        public static ModuleBackup BackupFixed<T>(
            SaveTypeModule module,
            NativeArray<Entity> updateTargets,
            EntityManager em,
            Allocator allocator,
            bool hasEnabled,
            System.Func<Entity, EntityManager, bool> readEnabled)
            where T : unmanaged, IComponentData
        {
            int n = updateTargets.Length;
            int stride = UnsafeUtility.SizeOf<T>();

            var values = new NativeArray<byte>(n * stride, allocator);
            var presence = new NativeArray<byte>(n, allocator);
            var enabled = hasEnabled ? new NativeArray<byte>(n, allocator) : default;

            byte* vptr = (byte*)values.GetUnsafePtr();

            for (int i = 0; i < n; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<T>(e))
                {
                    presence[i] = 0;
                    continue;
                }

                presence[i] = 1;
                var v = em.GetComponentData<T>(e);
                byte* src = (byte*)UnsafeUtility.AddressOf(ref v);
                byte* dst = vptr + i * stride;
                for (int b = 0; b < stride; b++) dst[b] = src[b];

                if (hasEnabled)
                    enabled[i] = readEnabled(e, em) ? (byte)1 : (byte)0;
            }

            return new ModuleBackup
            {
                Module = module,
                Values = values,
                Presence = presence,
                Enabled = enabled,
                Count = n,
            };
        }

        public static void RestoreFixed<T>(
            NativeArray<Entity> updateTargets,
            in ModuleBackup backup,
            EntityManager em,
            bool hasEnabled,
            System.Action<Entity, EntityManager, bool> writeEnabled)
            where T : unmanaged, IComponentData
        {
            int n = backup.Count;
            int stride = UnsafeUtility.SizeOf<T>();
            byte* vptr = (byte*)backup.Values.GetUnsafeReadOnlyPtr();

            for (int i = 0; i < n; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null || !em.Exists(e)) continue;

                if (backup.Presence[i] == 0)
                {
                    if (em.HasComponent<T>(e))
                        em.RemoveComponent<T>(e);
                    continue;
                }

                T value = default;
                byte* dst = (byte*)UnsafeUtility.AddressOf(ref value);
                byte* src = vptr + i * stride;
                for (int b = 0; b < stride; b++) dst[b] = src[b];

                if (em.HasComponent<T>(e))
                    em.SetComponentData(e, value);
                else
                    em.AddComponentData(e, value);

                if (hasEnabled)
                    writeEnabled(e, em, backup.Enabled[i] != 0);
            }
        }

        public static ModuleBackup BackupBuffer<T>(
            SaveTypeModule module,
            NativeArray<Entity> updateTargets,
            EntityManager em,
            Allocator allocator,
            bool hasEnabled,
            System.Func<Entity, EntityManager, bool> readEnabled)
            where T : unmanaged, IBufferElementData
        {
            int n = updateTargets.Length;
            int stride = UnsafeUtility.SizeOf<T>();

            var presence = new NativeArray<byte>(n, allocator);
            var bufferCounts = new NativeArray<int>(n, allocator);
            var enabled = hasEnabled ? new NativeArray<byte>(n, allocator) : default;

            int totalElements = 0;
            for (int i = 0; i < n; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<T>(e))
                {
                    presence[i] = 0;
                    continue;
                }

                presence[i] = 1;
                var buf = em.GetBuffer<T>(e, isReadOnly: true);
                int c = buf.Length;
                bufferCounts[i] = c;
                totalElements += c;

                if (hasEnabled)
                    enabled[i] = readEnabled(e, em) ? (byte)1 : (byte)0;
            }

            var values = new NativeArray<byte>(totalElements * stride, allocator);
            int offset = 0;
            byte* vptr = (byte*)values.GetUnsafePtr();

            for (int i = 0; i < n; i++)
            {
                if (presence[i] == 0) continue;
                var e = updateTargets[i];
                var buf = em.GetBuffer<T>(e, isReadOnly: true);
                int bytes = bufferCounts[i] * stride;
                if (bytes > 0)
                {
                    byte* src = (byte*)buf.AsNativeArray().GetUnsafeReadOnlyPtr();
                    byte* dst = vptr + offset;
                    for (int b = 0; b < bytes; b++) dst[b] = src[b];
                }
                offset += bytes;
            }

            return new ModuleBackup
            {
                Module = module,
                Values = values,
                Presence = presence,
                Enabled = enabled,
                BufferCounts = bufferCounts,
                Count = n,
            };
        }

        public static void RestoreBuffer<T>(
            NativeArray<Entity> updateTargets,
            in ModuleBackup backup,
            EntityManager em,
            bool hasEnabled,
            System.Action<Entity, EntityManager, bool> writeEnabled)
            where T : unmanaged, IBufferElementData
        {
            int n = backup.Count;
            int stride = UnsafeUtility.SizeOf<T>();
            byte* vptr = (byte*)backup.Values.GetUnsafeReadOnlyPtr();

            int offset = 0;
            for (int i = 0; i < n; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null || !em.Exists(e))
                {
                    if (backup.Presence[i] != 0)
                        offset += backup.BufferCounts[i] * stride;
                    continue;
                }

                if (backup.Presence[i] == 0)
                {
                    if (em.HasComponent<T>(e))
                        em.RemoveComponent<T>(e);
                    continue;
                }

                int c = backup.BufferCounts[i];

                DynamicBuffer<T> buf;
                if (em.HasComponent<T>(e))
                {
                    buf = em.GetBuffer<T>(e, isReadOnly: false);
                    buf.Clear();
                }
                else
                {
                    buf = em.AddBuffer<T>(e);
                }

                if (stride > 0 && c > 0)
                {
                    byte* src = vptr + offset;
                    for (int k = 0; k < c; k++)
                    {
                        T value = default;
                        byte* valPtr = (byte*)&value;
                        byte* srcPtr = src + k * stride;
                        for (int b = 0; b < stride; b++)
                            valPtr[b] = srcPtr[b];
                        buf.Add(value);
                    }
                }
                else if (stride == 0)
                {
                    for (int k = 0; k < c; k++)
                        buf.Add(default);
                }

                if (hasEnabled)
                    writeEnabled(e, em, backup.Enabled[i] != 0);

                offset += c * stride;
            }
        }

        public static void BackupShared<T>(
            NativeArray<Entity> updateTargets,
            EntityManager em,
            Allocator allocator,
            out NativeArray<byte> values,
            out NativeArray<byte> presence)
            where T : unmanaged, ISharedComponentData
        {
            int n = updateTargets.Length;
            int stride = UnsafeUtility.SizeOf<T>();

            values = new NativeArray<byte>(n * stride, allocator);
            presence = new NativeArray<byte>(n, allocator);

            byte* vptr = (byte*)values.GetUnsafePtr();

            for (int i = 0; i < n; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null || !em.Exists(e) || !em.HasComponent<T>(e))
                {
                    presence[i] = 0;
                    continue;
                }

                presence[i] = 1;
                var v = em.GetSharedComponent<T>(e);
                byte* src = (byte*)UnsafeUtility.AddressOf(ref v);
                byte* dst = vptr + i * stride;
                for (int b = 0; b < stride; b++) dst[b] = src[b];
            }
        }

        public static void RestoreShared<T>(
            NativeArray<Entity> updateTargets,
            in ModuleBackup backup,
            EntityManager em)
            where T : unmanaged, ISharedComponentData
        {
            int n = backup.Count;
            int stride = UnsafeUtility.SizeOf<T>();
            byte* vptr = (byte*)backup.Values.GetUnsafeReadOnlyPtr();

            for (int i = 0; i < n; i++)
            {
                var e = updateTargets[i];
                if (e == Entity.Null || !em.Exists(e)) continue;

                if (backup.Presence[i] == 0)
                {
                    if (em.HasComponent<T>(e))
                        em.RemoveComponent<T>(e);
                    continue;
                }

                T value = default;
                byte* dst = (byte*)UnsafeUtility.AddressOf(ref value);
                byte* src = vptr + i * stride;
                for (int b = 0; b < stride; b++) dst[b] = src[b];

                if (em.HasComponent<T>(e))
                    em.SetSharedComponent(e, value);
                else
                    em.AddSharedComponent(e, value);
            }
        }
    }
}
