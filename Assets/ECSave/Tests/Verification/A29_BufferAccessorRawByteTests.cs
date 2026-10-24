using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Tests
{
    /// <summary>
    /// A-29（仮）：BufferAccessor の生バイト経路検証。
    /// A-22 では IComponentData の経路のみ確認済み。Buffer は未検証。
    /// 本テストで、以下を確認する：
    ///   1. chunk.GetBufferAccessor(ref handle) で DynamicBuffer<T> が得られる
    ///   2. DynamicBuffer<T>.AsNativeArray() から GetUnsafeReadOnlyPtr() で byte* が得られる
    ///   3. 型付きアクセスと生バイト読み取り値が一致する
    ///   4. 要素数が Entity ごとに異なっても正しく取得できる
    ///   5. 空 Buffer（要素数 0）も取得できる
    ///   6. 構造変更後に再取得すると、新しい要素数が正しく反映される（Chunk をキャッシュしない）
    /// </summary>
    public unsafe class A29_BufferAccessorRawByteTests
    {
        private World _world;
        private EntityManager _em;

        public struct Item : IBufferElementData
        {
            public int Id;
            public int Count;
        }

        [SetUp]
        public void SetUp()
        {
            _world = new World("A29BufferWorld");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void BufferAccessor_ProvidesDynamicBuffer()
        {
            var e = _em.CreateEntity();
            var buf = _em.AddBuffer<Item>(e);
            buf.Add(new Item { Id = 1, Count = 10 });
            buf.Add(new Item { Id = 2, Count = 20 });

            var query = _em.CreateEntityQuery(typeof(Item));
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);

            var handle = _em.GetBufferTypeHandle<Item>(isReadOnly: true);

            Assert.Greater(chunks.Length, 0);
            var accessor = chunks[0].GetBufferAccessor(ref handle);
            Assert.AreEqual(1, accessor.Length);

            var dynamicBuffer = accessor[0];
            Assert.AreEqual(2, dynamicBuffer.Length);
            Assert.AreEqual(1, dynamicBuffer[0].Id);
            Assert.AreEqual(20, dynamicBuffer[1].Count);
        }

        [Test]
        public void BufferAccessor_RawBytes_MatchTypedValues()
        {
            var e = _em.CreateEntity();
            var buf = _em.AddBuffer<Item>(e);
            buf.Add(new Item { Id = 1, Count = 100 });
            buf.Add(new Item { Id = 2, Count = 200 });
            buf.Add(new Item { Id = 3, Count = 300 });

            var query = _em.CreateEntityQuery(typeof(Item));
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);

            var handle = _em.GetBufferTypeHandle<Item>(isReadOnly: true);
            var accessor = chunks[0].GetBufferAccessor(ref handle);
            var dynamicBuffer = accessor[0];

            var array = dynamicBuffer.AsNativeArray();
            Assert.AreEqual(3, array.Length);

            byte* src = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(array);
            Assert.IsTrue(src != null, "Raw byte pointer must not be null for non-empty buffer");

            int stride = UnsafeUtility.SizeOf<Item>();
            for (int i = 0; i < array.Length; i++)
            {
                int id = *(int*)(src + i * stride);
                int count = *(int*)(src + i * stride + 4);
                Assert.AreEqual(array[i].Id, id, $"Id mismatch at {i}");
                Assert.AreEqual(array[i].Count, count, $"Count mismatch at {i}");
            }
        }

        [Test]
        public void BufferAccessor_VariableElementCounts()
        {
            // 3 Entity、要素数 0, 1, 5
            var e0 = _em.CreateEntity();
            _em.AddBuffer<Item>(e0); // 空

            var e1 = _em.CreateEntity();
            var buf1 = _em.AddBuffer<Item>(e1);
            buf1.Add(new Item { Id = 1, Count = 10 });

            var e2 = _em.CreateEntity();
            var buf2 = _em.AddBuffer<Item>(e2);
            for (int i = 0; i < 5; i++)
                buf2.Add(new Item { Id = 100 + i, Count = i });

            var query = _em.CreateEntityQuery(typeof(Item));
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);

            var handle = _em.GetBufferTypeHandle<Item>(isReadOnly: true);

            int totalElements = 0;
            int totalEntities = 0;
            for (int c = 0; c < chunks.Length; c++)
            {
                var accessor = chunks[c].GetBufferAccessor(ref handle);
                for (int i = 0; i < accessor.Length; i++)
                {
                    totalEntities++;
                    totalElements += accessor[i].Length;
                }
            }

            Assert.AreEqual(3, totalEntities);
            Assert.AreEqual(6, totalElements); // 0 + 1 + 5
        }

        [Test]
        public void BufferAccessor_EmptyBuffer_HasZeroLength()
        {
            var e = _em.CreateEntity();
            _em.AddBuffer<Item>(e);

            var query = _em.CreateEntityQuery(typeof(Item));
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);

            var handle = _em.GetBufferTypeHandle<Item>(isReadOnly: true);
            var accessor = chunks[0].GetBufferAccessor(ref handle);
            var buf = accessor[0];

            Assert.AreEqual(0, buf.Length);

            // 空でも AsNativeArray / GetUnsafeReadOnlyPtr が例外を投げない
            var array = buf.AsNativeArray();
            Assert.AreEqual(0, array.Length);
            byte* src = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(array);
            Assert.IsTrue(src == null || array.Length == 0);
        }

        // ★ 修正: 旧テスト名は「ポインタが変わる」ことを示唆していたが、
        //   実際には「再取得すると新しい長さが読める」ことしか検証していなかった。
        //   テスト名を意図に合わせて Reacquire_AfterStructuralChange_ReturnsUpdatedLength に変更し、
        //   ポインタの変化は「保証しない」ことを明記した。
        [Test]
        public void Reacquire_AfterStructuralChange_ReturnsUpdatedLength()
        {
            var e = _em.CreateEntity();
            var buf = _em.AddBuffer<Item>(e);
            buf.Add(new Item { Id = 1, Count = 10 });

            var query = _em.CreateEntityQuery(typeof(Item));

            // 1 回目の取得
            {
                using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);
                var handle = _em.GetBufferTypeHandle<Item>(isReadOnly: true);
                var accessor = chunks[0].GetBufferAccessor(ref handle);
                Assert.AreEqual(1, accessor[0].Length);
            }

            // 構造変更：要素を追加
            var buf2 = _em.GetBuffer<Item>(e);
            for (int i = 0; i < 100; i++)
                buf2.Add(new Item { Id = i, Count = i });

            // 再取得すると新しい長さが見える（Chunk をキャッシュしないことの確認）
            using var chunks2 = query.ToArchetypeChunkArray(Allocator.TempJob);
            var h2 = _em.GetBufferTypeHandle<Item>(isReadOnly: true);
            var a2 = chunks2[0].GetBufferAccessor(ref h2);
            Assert.AreEqual(101, a2[0].Length,
                "構造変更後は Chunk をキャッシュせず再取得する必要がある");
        }
    }
}
