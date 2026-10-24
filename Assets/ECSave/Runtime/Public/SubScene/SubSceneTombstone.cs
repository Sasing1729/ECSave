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

            unsafe
            {
                byte* basePtr = (byte*)buf.GetUnsafePtr();
                int total = buf.Length;
                int pos = 0;
                fixed (TIdentity* newKeyPtr = &id)
                {
                    while (pos + 12 <= total)
                    {
                        ulong h = *(ulong*)(basePtr + pos);
                        int len = *(int*)(basePtr + pos + 8);
                        if (len < 0 || pos + 12 + len > total) break;

                        if (h == typeHash && len == stride)
                        {
                            byte* existing = basePtr + pos + 12;
                            bool eq = true;
                            for (int b = 0; b < stride; b++)
                            {
                                if (existing[b] != ((byte*)newKeyPtr)[b]) { eq = false; break; }
                            }
                            if (eq) return;
                        }
                        pos += 12 + len;
                    }
                }
            }

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

        public static void ClearBuffer(World world)
        {
            if (world == null || !world.IsCreated) return;
            if (!SubSceneTombstoneRegistry.TryGetSingleton(world.EntityManager, out var singleton))
                return;
            SubSceneTombstoneRegistry.ClearBuffer(world.EntityManager, singleton);
        }

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

        /// <summary>
        /// Review #11: Domain Reload 無効時の静的状態リセット。
        /// セッション間でハンドラ辞書が残ると、Register が早期 return して
        /// 新しいハンドラが登録されない（ホットリロード時に型 ID が変わる場合の罠）。
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void ResetOnLoad()
        {
            s_handlers.Clear();
        }
    }
}
