using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

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

        /// <summary>
        /// 型名のハッシュ（Tombstone レコードの型識別子、および
        /// Tombstone セクション名のハッシュ）。
        ///
        /// <para>
        /// 旧実装は <c>SchemaHashCoreLite.HashUtf8</c> という専用実装を持っていたが、
        /// <see cref="SchemaHashCore.HashUtf8"/> と完全に同一の仕様（FNV-1a 64bit、
        /// UTF-8 エンコード）であるべきにも関わらず、サロゲートペアの扱いに差があった
        /// （Lite 版は非 BMP 文字を 3 byte で誤エンコード）。
        /// </para>
        /// <para>
        /// 冗長な実装を削除し、共通の <see cref="SchemaHashCore.HashUtf8"/> に委譲する。
        /// 型名に非 BMP 文字（絵文字等）を含まない限り、ハッシュ値は旧実装と一致する。
        /// </para>
        /// </summary>
        public static ulong ComputeTypeHash(string fullTypeName)
            => SchemaHashCore.HashUtf8(fullTypeName);
    }
}
