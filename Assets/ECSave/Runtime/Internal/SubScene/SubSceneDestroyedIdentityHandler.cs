using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal.SubScene
{
    internal abstract class SubSceneDestroyedIdentityHandler
    {
        public abstract Type IdentityType { get; }
        public abstract string FullTypeName { get; }
        public abstract int KeyStride { get; }
        internal abstract ulong TypeHash { get; }

        public abstract IReadOnlyList<byte[]> Snapshot(World world);

        public abstract void RestoreToRegistry(
            EntityManager em, Entity singleton, IReadOnlyList<byte[]> keys);

        public abstract void CollectMatches(
            EntityManager em, Entity singleton, NativeList<Entity> output);
    }

    internal sealed unsafe class SubSceneDestroyedIdentityHandler<TIdentity>
        : SubSceneDestroyedIdentityHandler
        where TIdentity : unmanaged, IComponentData
    {
        private static readonly ulong s_typeHash =
            SubSceneTombstoneRegistry.ComputeTypeHash(typeof(TIdentity).FullName);

        public override Type IdentityType => typeof(TIdentity);
        public override string FullTypeName => typeof(TIdentity).FullName;
        public override int KeyStride => UnsafeUtility.SizeOf<TIdentity>();
        internal override ulong TypeHash => s_typeHash;

        public override IReadOnlyList<byte[]> Snapshot(World world)
        {
            var em = world.EntityManager;
            if (!SubSceneTombstoneRegistry.TryGetSingleton(em, out var singleton))
                return Array.Empty<byte[]>();
            if (!em.HasBuffer<SubSceneTombstoneByte>(singleton))
                return Array.Empty<byte[]>();

            var buf = em.GetBuffer<SubSceneTombstoneByte>(singleton, true);
            int total = buf.Length;
            if (total == 0) return Array.Empty<byte[]>();

            byte* ptr = (byte*)buf.GetUnsafeReadOnlyPtr();
            int pos = 0;
            var result = new List<byte[]>();

            while (pos + 12 <= total)
            {
                ulong hash = *(ulong*)(ptr + pos);
                int len = *(int*)(ptr + pos + 8);
                pos += 12;
                if (len < 0 || pos + len > total) break;

                if (hash == s_typeHash)
                {
                    var bytes = new byte[len];
                    for (int b = 0; b < len; b++) bytes[b] = ptr[pos + b];
                    result.Add(bytes);
                }

                pos += len;
            }
            return result;
        }

        public override void RestoreToRegistry(
            EntityManager em, Entity singleton, IReadOnlyList<byte[]> keys)
        {
            if (keys == null || keys.Count == 0) return;

            int stride = KeyStride;
            var buf = SubSceneTombstoneRegistry.GetOrCreateBuffer(em, singleton);

            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (key == null || key.Length != stride) continue;

                int oldLen = buf.Length;
                buf.ResizeUninitialized(oldLen + 12 + stride);
                byte* dst = (byte*)buf.GetUnsafePtr() + oldLen;
                *(ulong*)dst = s_typeHash;
                *(int*)(dst + 8) = stride;
                fixed (byte* src = key)
                    UnsafeUtility.MemCpy(dst + 12, src, stride);
            }
        }

        public override void CollectMatches(
            EntityManager em, Entity singleton, NativeList<Entity> output)
        {
            if (!em.Exists(singleton)) return;
            if (!em.HasBuffer<SubSceneTombstoneByte>(singleton)) return;

            var buf = em.GetBuffer<SubSceneTombstoneByte>(singleton, true);
            int total = buf.Length;
            if (total == 0) return;

            int stride = KeyStride;
            byte* ptr = (byte*)buf.GetUnsafeReadOnlyPtr();

            // 1st pass: 自分の型のキーだけを連続領域に抽出
            var ourKeys = new NativeList<byte>(total, Allocator.TempJob);
            try
            {
                int pos = 0;
                while (pos + 12 <= total)
                {
                    ulong hash = *(ulong*)(ptr + pos);
                    int len = *(int*)(ptr + pos + 8);
                    pos += 12;
                    if (len < 0 || pos + len > total) break;

                    if (hash == s_typeHash && len == stride)
                    {
                        for (int b = 0; b < len; b++)
                            ourKeys.Add(ptr[pos + b]);
                    }
                    pos += len;
                }

                int keyCount = ourKeys.Length / stride;
                if (keyCount == 0) return;

                byte* keyPtr = (byte*)ourKeys.GetUnsafeReadOnlyPtr();

                // 2nd pass: Entity を走査して一致するものを追加
                using var query = em.CreateEntityQuery(ComponentType.ReadOnly<TIdentity>());
                using var entities = query.ToEntityArray(Allocator.Temp);
                if (entities.Length == 0) return;

                for (int i = 0; i < entities.Length; i++)
                {
                    var e = entities[i];
                    var v = em.GetComponentData<TIdentity>(e);
                    byte* vp = (byte*)&v;

                    for (int k = 0; k < keyCount; k++)
                    {
                        byte* cand = keyPtr + k * stride;
                        bool eq = true;
                        for (int b = 0; b < stride; b++)
                        {
                            if (vp[b] != cand[b]) { eq = false; break; }
                        }
                        if (eq)
                        {
                            output.Add(e);
                            break;
                        }
                    }
                }
            }
            finally
            {
                if (ourKeys.IsCreated) ourKeys.Dispose();
            }
        }
    }
}
