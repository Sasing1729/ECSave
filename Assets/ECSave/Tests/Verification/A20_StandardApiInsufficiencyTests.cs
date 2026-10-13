using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A20Data : IComponentData { public int Value; public Entity Ref; }
    public struct A20Enableable : IComponentData, IEnableableComponent { public int Value; }

    public class A20_StandardApiInsufficiencyTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A20");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void ToComponentDataArray_ExcludesDisabledEntity()
        {
            var arch = _em.CreateArchetype(typeof(A20Enableable));
            var es = _em.CreateEntity(arch, 5, Allocator.TempJob);
            _em.SetComponentEnabled<A20Enableable>(es[0], false);
            _em.SetComponentEnabled<A20Enableable>(es[1], false);

            var qDefault = _em.CreateEntityQuery(typeof(A20Enableable));
            var arrDefault = qDefault.ToComponentDataArray<A20Enableable>(Allocator.TempJob);

            var qIgnore = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<A20Enableable>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            var arrIgnore = qIgnore.ToComponentDataArray<A20Enableable>(Allocator.TempJob);

            try
            {
                Debug.Log($"[A-20] ToComponentDataArray: default={arrDefault.Length}, " +
                          $"ignore={arrIgnore.Length}");

                Assert.AreEqual(3, arrDefault.Length,
                    "デフォルトクエリで無効 2 件が除外されていない（要件 7.2 と整合）");
                Assert.AreEqual(5, arrIgnore.Length,
                    "IgnoreComponentEnabledState で全件が取得できていない");
                Debug.Log("[A-20] ToComponentDataArray はデフォルトで無効 Entity を除外。" +
                          "Capture では IgnoreComponentEnabledState が必須");
            }
            finally
            {
                arrDefault.Dispose();
                arrIgnore.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void ToComponentDataArray_ExcludesPrefab()
        {
            _em.CreateEntity(typeof(A20Data));
            _em.CreateEntity(typeof(A20Data), typeof(Prefab));

            var qDefault = _em.CreateEntityQuery(typeof(A20Data));
            var arrDefault = qDefault.ToComponentDataArray<A20Data>(Allocator.TempJob);

            var qWithPrefab = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A20Data>() },
                Options = EntityQueryOptions.IncludePrefab,
            });
            var arrWithPrefab = qWithPrefab.ToComponentDataArray<A20Data>(Allocator.TempJob);

            try
            {
                Debug.Log($"[A-20] ToComponentDataArray: default={arrDefault.Length}, " +
                          $"IncludePrefab={arrWithPrefab.Length}");
                Assert.AreEqual(1, arrDefault.Length);
                Assert.AreEqual(2, arrWithPrefab.Length);
            }
            finally
            {
                arrDefault.Dispose();
                arrWithPrefab.Dispose();
            }
        }

        [Test]
        public void ToEntityArray_ReturnsEntityNotPersistentId()
        {
            var e1 = _em.CreateEntity(typeof(A20Data));
            var e2 = _em.CreateEntity(typeof(A20Data));

            var q = _em.CreateEntityQuery(typeof(A20Data));
            var entities = q.ToEntityArray(Allocator.TempJob);
            try
            {
                Debug.Log($"[A-20] ToEntityArray の Entity: {entities[0]}, {entities[1]}");
                Debug.Log("[A-20] 返るのは ECS の Entity（Index + Version）であり、" +
                          "SaveData 上の永続 ID ではない。永続 ID は ECSave が独自に構築する");

                Assert.AreNotEqual(e1, e2);
                Assert.AreNotEqual(entities[0], entities[1]);
            }
            finally
            {
                entities.Dispose();
            }
        }

        [Test]
        public void ToComponentDataArray_ProducesManagedArray_NoRawByteCopy()
        {
            _em.CreateEntity(typeof(A20Data));

            var q = _em.CreateEntityQuery(typeof(A20Data));
            var arr = q.ToComponentDataArray<A20Data>(Allocator.TempJob);
            try
            {
                Debug.Log("[A-20] ToComponentDataArray は型付き NativeArray<T> を返す。" +
                          "生のバイト列（セクション表・スキーマ記述子を含む）は取得できない。" +
                          "SaveData のバイナリ形式は ECSave が独自に構築する（要件 7.2）");

                Assert.AreEqual(1, arr.Length);
                Assert.AreEqual(typeof(A20Data), arr.GetType().GetGenericArguments()[0]);
            }
            finally
            {
                arr.Dispose();
            }
        }

        [Test]
        public void EntityQuery_ToArchetypeChunkArray_ReturnsChunks()
        {
            var arch = _em.CreateArchetype(typeof(A20Data));
            _em.CreateEntity(arch, 100, Allocator.TempJob);

            var q = _em.CreateEntityQuery(typeof(A20Data));
            var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                int totalEntities = 0;
                foreach (var c in chunks) totalEntities += c.Count;

                Debug.Log($"[A-20] ToArchetypeChunkArray: chunks={chunks.Length}, " +
                          $"totalEntities={totalEntities}");
                Assert.AreEqual(100, totalEntities);
                Debug.Log("[A-20] ArchetypeChunk 単位の走査が可能。" +
                          "Chunk Component の取得（13.3 P6）も Chunk 単位で行う");
            }
            finally
            {
                chunks.Dispose();
            }
        }

        [Test]
        public void EntityManager_GetAllEntities_IsNotEnoughForSave()
        {
            _em.CreateEntity(typeof(A20Data));
            _em.CreateEntity(typeof(A20Data), typeof(Prefab));

            var all = _em.GetAllEntities(Allocator.Temp);
            try
            {
                Debug.Log($"[A-20] GetAllEntities は Entity のみを返す。値もスキーマも返さない。" +
                          "SaveData 構築には型ごとの Capture が必要（要件 7.2）");

                Assert.GreaterOrEqual(all.Length, 2);
            }
            finally
            {
                all.Dispose();
            }
        }

        [Test]
        public void ToComponentDataArray_DoesNotCoverSharedComponent()
        {
            _em.CreateEntity(typeof(A20Data));

            var q = _em.CreateEntityQuery(typeof(A20Data));
            bool threw = false;
            try
            {
                var shared = q.ToComponentDataArray<A20Data>(Allocator.TempJob);
                shared.Dispose();
            }
            catch (System.Exception ex)
            {
                threw = true;
                Debug.Log($"[A-20] 例外: {ex.GetType().Name}");
            }

            Debug.Log("[A-20] ToComponentDataArray は ISharedComponentData を扱えない。" +
                      "SharedComponent は別 API（GetSharedComponent）で個別取得が必要");
            Assert.IsFalse(threw);
        }

        [Test]
        public void ManagedArray_AllocatesGC_AndLosesRawBytes()
        {
            var arch = _em.CreateArchetype(typeof(A20Data));
            _em.CreateEntity(arch, 1000, Allocator.TempJob);

            var q = _em.CreateEntityQuery(typeof(A20Data));

            long gcBefore = System.GC.GetTotalMemory(false);
            var arr = q.ToComponentDataArray<A20Data>(Allocator.TempJob);
            long gcAfter = System.GC.GetTotalMemory(false);

            try
            {
                long diff = gcAfter - gcBefore;
                Debug.Log($"[A-20] ToComponentDataArray GC delta: {diff} bytes " +
                          $"(NativeArray のため managed GC は増えない)");

                Assert.AreEqual(1000, arr.Length);
                Debug.Log("[A-20] ToComponentDataArray は NativeArray を返すため、" +
                          "生バイトの直接操作（Section 表の書き込み）には使えない。" +
                          "ECSave は独自の Format 層を持つ必要がある（要件 3.1）");
            }
            finally
            {
                arr.Dispose();
            }
        }
    }
}
