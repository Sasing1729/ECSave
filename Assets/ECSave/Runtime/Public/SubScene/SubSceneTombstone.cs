using System;
using System.Collections.Generic;
using ECSave.Internal.SubScene;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave
{
    public static class SubSceneTombstone
    {
        private static readonly Dictionary<Type, SubSceneDestroyedIdentityHandler> s_handlers
            = new Dictionary<Type, SubSceneDestroyedIdentityHandler>();

        public static void Register<TIdentity>()
            where TIdentity : unmanaged, IComponentData
        {
            var t = typeof(TIdentity);
            if (s_handlers.ContainsKey(t)) return;
            s_handlers[t] = new SubSceneDestroyedIdentityHandler<TIdentity>();
        }

        public static void MarkDestroyed<TIdentity>(EntityManager em, in TIdentity id)
            where TIdentity : unmanaged, IComponentData
        {
            Register<TIdentity>();

            var singleton = SubSceneTombstoneRegistry.GetOrCreate(em);
            var buf = SubSceneTombstoneRegistry.GetOrCreateBuffer(em, singleton);

            int stride = UnsafeUtility.SizeOf<TIdentity>();
            ulong typeHash = SubSceneTombstoneRegistry.ComputeTypeHash(typeof(TIdentity).FullName);

            int oldLen = buf.Length;
            buf.ResizeUninitialized(oldLen + 12 + stride);

            unsafe
            {
                byte* dst = (byte*)buf.GetUnsafePtr() + oldLen;
                *(ulong*)dst = typeHash;
                *(int*)(dst + 8) = stride;

                fixed (TIdentity* src = &id)
                    UnsafeUtility.MemCpy(dst + 12, src, stride);
            }
        }

        /// <summary>Registry の全 Buffer をクリアする（テスト・リセット用）。</summary>
        public static void ClearBuffer(World world)
        {
            if (world == null || !world.IsCreated) return;
            if (!SubSceneTombstoneRegistry.TryGetSingleton(world.EntityManager, out var singleton))
                return;
            SubSceneTombstoneRegistry.ClearBuffer(world.EntityManager, singleton);
        }

        /// <summary>Registry の singleton Entity ごと破棄する。</summary>
        public static void Clear(World world)
        {
            if (world == null || !world.IsCreated) return;
            SubSceneTombstoneRegistry.Destroy(world.EntityManager);
        }

        internal static IReadOnlyList<SubSceneDestroyedIdentityHandler> GetRegisteredHandlers()
        {
            var list = new List<SubSceneDestroyedIdentityHandler>(s_handlers.Count);
            foreach (var h in s_handlers.Values) list.Add(h);
            return list;
        }

        internal static void ClearForTests()
        {
            s_handlers.Clear();
        }
    }
}
