using Unity.Collections;
using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Internal.SubScene
{
    internal static unsafe class SubSceneTombstoneRegistry
    {
        public static Entity GetOrCreate(EntityManager em)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<SubSceneTombstoneRegistryTag>());

            if (query.TryGetSingletonEntity<SubSceneTombstoneRegistryTag>(out var singleton))
                return singleton;

            singleton = em.CreateEntity();
            em.AddComponent<SubSceneTombstoneRegistryTag>(singleton);
            em.AddBuffer<SubSceneTombstoneByte>(singleton);
            return singleton;
        }

        public static bool TryGetSingleton(EntityManager em, out Entity singleton)
        {
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<SubSceneTombstoneRegistryTag>());

            return query.TryGetSingletonEntity<SubSceneTombstoneRegistryTag>(out singleton);
        }

        public static DynamicBuffer<SubSceneTombstoneByte> GetOrCreateBuffer(
            EntityManager em, Entity singleton)
        {
            if (em.HasBuffer<SubSceneTombstoneByte>(singleton))
                return em.GetBuffer<SubSceneTombstoneByte>(singleton);

            return em.AddBuffer<SubSceneTombstoneByte>(singleton);
        }

        public static void ClearBuffer(EntityManager em, Entity singleton)
        {
            if (!em.Exists(singleton)) return;
            if (!em.HasBuffer<SubSceneTombstoneByte>(singleton)) return;

            var buf = em.GetBuffer<SubSceneTombstoneByte>(singleton);
            buf.Clear();
        }

        public static void Destroy(EntityManager em)
        {
            if (TryGetSingleton(em, out var singleton))
                em.DestroyEntity(singleton);
        }

        public static ulong ComputeTypeHash(string fullTypeName)
            => SchemaHashCoreLite.HashUtf8(fullTypeName);
    }

    internal static class SchemaHashCoreLite
    {
        public const ulong FnvOffsetBasis = 0xcbf29ce484222325UL;
        public const ulong FnvPrime = 0x100000001b3UL;

        public static ulong HashUtf8(string s, ulong seed = FnvOffsetBasis)
        {
            if (string.IsNullOrEmpty(s)) return seed;
            ulong h = seed;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x80) { h ^= (byte)c; h *= FnvPrime; }
                else if (c < 0x800)
                {
                    h ^= (byte)(0xC0 | (c >> 6)); h *= FnvPrime;
                    h ^= (byte)(0x80 | (c & 0x3F)); h *= FnvPrime;
                }
                else
                {
                    h ^= (byte)(0xE0 | (c >> 12)); h *= FnvPrime;
                    h ^= (byte)(0x80 | ((c >> 6) & 0x3F)); h *= FnvPrime;
                    h ^= (byte)(0x80 | (c & 0x3F)); h *= FnvPrime;
                }
            }
            return h;
        }
    }
}
