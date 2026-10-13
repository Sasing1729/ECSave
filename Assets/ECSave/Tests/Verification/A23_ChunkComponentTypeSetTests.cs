using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests
{
    public struct A23Comp : IComponentData { public int Value; }
    public struct A23Tag : IComponentData { }
    public struct A23ChunkA : IComponentData { public int Value; }
    public struct A23ChunkB : IComponentData { public float Value; }

    /// <summary>
    /// A-23: Chunk Component を含む ComponentTypeSet による一括の追加・除去（内部設計 8.5節 P4、12.3節 ロールバック）。
    /// 確認: (1) NativeArray&lt;Entity&gt; 版と EntityQuery 版で追加できる (2) 通常型との混在 (3) 新規値は default
    ///       (4) 同一アーキタイプの Entity は同一 Chunk を共有 (5) 除去（T の追加の取り消し）
    ///       (6) CreateArchetype に Chunk Component を含め CreateEntity(archetype, NativeArray) で一括生成（P3）
    ///       (7) 構造変更で別 Chunk に移った Entity の値（13.3節の前提）
    /// </summary>
    public class A23_ChunkComponentTypeSetTests
    {
        World _world;
        EntityManager _em;

        [SetUp] public void SetUp() { _world = new World("A23"); _em = _world.EntityManager; }
        [TearDown] public void TearDown() { if (_world != null && _world.IsCreated) _world.Dispose(); }

        NativeArray<Entity> Create(int n)
        {
            var arch = _em.CreateArchetype(typeof(A23Comp));
            return _em.CreateEntity(arch, n, Allocator.Temp);
        }

        [Test]
        public void BatchAdd_NativeArray_AddsChunkComponents_WithDefaultValue()
        {
            var es = Create(10);
            var set = new ComponentTypeSet(ComponentType.ChunkComponent<A23ChunkA>(), ComponentType.ChunkComponent<A23ChunkB>());
            _em.AddComponent(es, set);

            for (int i = 0; i < es.Length; i++)
            {
                Assert.IsTrue(_em.HasChunkComponent<A23ChunkA>(es[i]));
                Assert.IsTrue(_em.HasChunkComponent<A23ChunkB>(es[i]));
                Assert.AreEqual(0, _em.GetChunkComponentData<A23ChunkA>(es[i]).Value, "新規値は default のはず");
            }
        }

        [Test]
        public void BatchAdd_MixedSet_NormalAndChunk()
        {
            var es = Create(10);
            var set = new ComponentTypeSet(ComponentType.ReadWrite<A23Tag>(), ComponentType.ChunkComponent<A23ChunkA>());
            _em.AddComponent(es, set);
            for (int i = 0; i < es.Length; i++)
            {
                Assert.IsTrue(_em.HasComponent<A23Tag>(es[i]));
                Assert.IsTrue(_em.HasChunkComponent<A23ChunkA>(es[i]));
            }
        }

        [Test]
        public void BatchAdd_QueryOverload()
        {
            Create(20);
            var q = _em.CreateEntityQuery(typeof(A23Comp));
            _em.AddComponent(q, new ComponentTypeSet(ComponentType.ChunkComponent<A23ChunkA>()));
            using var all = q.ToEntityArray(Allocator.Temp);
            Assert.AreEqual(20, all.Length);
            foreach (var e in all) Assert.IsTrue(_em.HasChunkComponent<A23ChunkA>(e));
        }

        [Test]
        public void SameArchetype_SharesChunk_AndValueIsPerChunk()
        {
            var es = Create(10);
            _em.AddComponent(es, new ComponentTypeSet(ComponentType.ChunkComponent<A23ChunkA>()));
            var chunk = _em.GetChunk(es[0]);
            for (int i = 1; i < es.Length; i++) Assert.IsTrue(chunk == _em.GetChunk(es[i]), "同一アーキタイプで別 Chunk");

            _em.SetChunkComponentData(chunk, new A23ChunkA { Value = 42 });
            for (int i = 0; i < es.Length; i++)
                Assert.AreEqual(42, _em.GetChunkComponentData<A23ChunkA>(es[i]).Value);
        }

        [Test]
        public void BatchRemove_ChunkComponents_ViaTypeSet()
        {
            var es = Create(10);
            var set = new ComponentTypeSet(ComponentType.ChunkComponent<A23ChunkA>(), ComponentType.ChunkComponent<A23ChunkB>());
            _em.AddComponent(es, set);
            _em.RemoveComponent(es, set);
            for (int i = 0; i < es.Length; i++)
            {
                Assert.IsFalse(_em.HasChunkComponent<A23ChunkA>(es[i]));
                Assert.IsFalse(_em.HasChunkComponent<A23ChunkB>(es[i]));
                Assert.IsTrue(_em.HasComponent<A23Comp>(es[i]), "通常 Component は残る");
            }
        }

        [Test]
        public void CreateEntity_FromArchetypeWithChunkComponent_Bulk()
        {
            var arch = _em.CreateArchetype(ComponentType.ReadWrite<A23Comp>(), ComponentType.ChunkComponent<A23ChunkA>());
            var es = _em.CreateEntity(arch, 1000, Allocator.Temp);
            Assert.AreEqual(1000, es.Length);
            foreach (var e in es)
            {
                Assert.IsTrue(_em.HasChunkComponent<A23ChunkA>(e));
                Assert.AreEqual(0, _em.GetChunkComponentData<A23ChunkA>(e).Value);
            }
        }

        [Test]
        public void MovedEntity_GetsNewChunk_WithDefaultValue()
        {
            var es = Create(5);
            _em.AddComponent(es, new ComponentTypeSet(ComponentType.ChunkComponent<A23ChunkA>()));
            _em.SetChunkComponentData(_em.GetChunk(es[0]), new A23ChunkA { Value = 42 });

            // es[4] だけを別アーキタイプへ移す → 新しい Chunk に入り、その Chunk の値は default
            _em.AddComponent<A23Tag>(es[4]);
            Assert.IsTrue(_em.HasChunkComponent<A23ChunkA>(es[4]), "構造変更後も Chunk Component は維持される");
            Assert.AreEqual(0, _em.GetChunkComponentData<A23ChunkA>(es[4]).Value, "移動先の新規 Chunk は default");
            Assert.AreEqual(42, _em.GetChunkComponentData<A23ChunkA>(es[0]).Value, "元の Chunk の値は不変");
            Assert.IsTrue(_em.GetChunk(es[0]) != _em.GetChunk(es[4]));
        }
    }
}
