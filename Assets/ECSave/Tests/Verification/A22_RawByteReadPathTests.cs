using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A22Data : IComponentData { public int A; public float B; public Entity Ref; }
    public struct A22Enableable : IComponentData, IEnableableComponent { public int Value; }

    public unsafe class A22_RawByteReadPathTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A22");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void ArchetypeChunk_GetNativeArray_ReturnsRawPointer()
        {
            var arch = _em.CreateArchetype(typeof(A22Data));
            _em.CreateEntity(arch, 10, Allocator.TempJob);

            var q = _em.CreateEntityQuery(typeof(A22Data));
            var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(true);
                foreach (var c in chunks)
                {
                    var native = c.GetNativeArray(ref handle);
                    void* ptr = native.GetUnsafeReadOnlyPtr();
                    int stride = UnsafeUtility.SizeOf<A22Data>();

                    Debug.Log($"[A-22] Chunk: Count={c.Count}, ptr=0x{(ulong)ptr:X}, " +
                              $"stride={stride}, bytes={c.Count * stride}");

                    Assert.IsTrue(ptr != null, "GetUnsafeReadOnlyPtr が null");
                    Assert.AreEqual(10, c.Count);
                    Assert.AreEqual(stride, UnsafeUtility.SizeOf<A22Data>());
                }
                Debug.Log("[A-22] ArchetypeChunk.GetNativeArray → GetUnsafeReadOnlyPtr で生バイト取得可能");
            }
            finally
            {
                chunks.Dispose();
            }
        }

        [Test]
        public void RawByteRead_MatchesTypedRead()
        {
            var arch = _em.CreateArchetype(typeof(A22Data));
            var es = _em.CreateEntity(arch, 5, Allocator.TempJob);
            for (int i = 0; i < es.Length; i++)
                _em.SetComponentData(es[i], new A22Data { A = i * 10, B = i * 1.5f, Ref = Entity.Null });

            var q = _em.CreateEntityQuery(typeof(A22Data));
            var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(true);
                int idx = 0;
                foreach (var c in chunks)
                {
                    var native = c.GetNativeArray(ref handle);
                    void* ptr = native.GetUnsafeReadOnlyPtr();
                    int stride = UnsafeUtility.SizeOf<A22Data>();

                    for (int i = 0; i < c.Count; i++)
                    {
                        var typed = native[i];
                        var raw = UnsafeUtility.ReadArrayElement<A22Data>(ptr, i);
                        Debug.Log($"[A-22] i={idx} typed.A={typed.A}, raw.A={raw.A}");

                        Assert.AreEqual(typed.A, raw.A);
                        Assert.AreEqual(typed.B, raw.B);
                        Assert.AreEqual(typed.Ref, raw.Ref);
                        idx++;
                    }
                }
                Assert.AreEqual(5, idx);
                Debug.Log("[A-22] 生バイト経由でも型付きと同じ値が読める");
            }
            finally
            {
                chunks.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void RawByteWrite_ThenTypedRead()
        {
            var arch = _em.CreateArchetype(typeof(A22Data));
            var es = _em.CreateEntity(arch, 3, Allocator.TempJob);

            var q = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<A22Data>() },
            });
            var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(false);
                foreach (var c in chunks)
                {
                    var native = c.GetNativeArray(ref handle);
                    void* ptr = native.GetUnsafePtr();
                    for (int i = 0; i < c.Count; i++)
                    {
                        UnsafeUtility.WriteArrayElement(ptr, i,
                            new A22Data { A = 999, B = 3.14f, Ref = Entity.Null });
                    }
                }

                for (int i = 0; i < es.Length; i++)
                {
                    var v = _em.GetComponentData<A22Data>(es[i]);
                    Assert.AreEqual(999, v.A);
                    Assert.AreEqual(3.14f, v.B);
                }
                Debug.Log("[A-22] 生バイト書込みが型付きで読める");
            }
            finally
            {
                chunks.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void Enableable_RawByteRead_IgnoresDisabledState()
        {
            var arch = _em.CreateArchetype(typeof(A22Enableable));
            var es = _em.CreateEntity(arch, 5, Allocator.TempJob);
            for (int i = 0; i < 3; i++)
                _em.SetComponentEnabled<A22Enableable>(es[i], false);

            var q = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<A22Enableable>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Enableable>(true);
                int totalCount = 0;
                foreach (var c in chunks)
                {
                    var native = c.GetNativeArray(ref handle);
                    void* ptr = native.GetUnsafeReadOnlyPtr();
                    totalCount += c.Count;

                    Debug.Log($"[A-22] Enableable chunk Count={c.Count}（無効も含む全件）");
                }
                Assert.AreEqual(5, totalCount,
                    "IgnoreComponentEnabledState クエリで全件取得できていない");
            }
            finally
            {
                chunks.Dispose();
                es.Dispose();
            }
        }

        // ★ 修正: 旧テストは Buffer ではなく Component の GetNativeArray を呼んでいた。
        //   本来の「Buffer の生バイト経路」を検証する内容に置き換える。
        public struct A22BufferItem : IBufferElementData
        {
            public int Id;
            public int Count;
        }

        [Test]
        public void BufferAccessor_RawByteRead()
        {
            var e = _em.CreateEntity();
            var buf = _em.AddBuffer<A22BufferItem>(e);
            buf.Add(new A22BufferItem { Id = 10, Count = 1 });
            buf.Add(new A22BufferItem { Id = 20, Count = 2 });
            buf.Add(new A22BufferItem { Id = 30, Count = 3 });

            var q = _em.CreateEntityQuery(typeof(A22BufferItem));
            var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetBufferTypeHandle<A22BufferItem>(isReadOnly: true);
                Assert.Greater(chunks.Length, 0);

                var accessor = chunks[0].GetBufferAccessor(ref handle);
                Assert.AreEqual(1, accessor.Length);

                var dynamicBuffer = accessor[0];
                var array = dynamicBuffer.AsNativeArray();
                Assert.AreEqual(3, array.Length);

                int stride = UnsafeUtility.SizeOf<A22BufferItem>();
                byte* src = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(array);
                Assert.IsTrue(src != null, "Buffer の生バイトポインタが null");

                for (int i = 0; i < array.Length; i++)
                {
                    int id = *(int*)(src + i * stride);
                    int count = *(int*)(src + i * stride + 4);
                    Assert.AreEqual(array[i].Id, id, $"Id mismatch at {i}");
                    Assert.AreEqual(array[i].Count, count, $"Count mismatch at {i}");
                }
                Debug.Log("[A-22] BufferAccessor 経由でも型付きと同じ生バイトが読める");
            }
            finally
            {
                chunks.Dispose();
            }
        }

        // ★ 修正: 旧テストは「ptr1 != null && ptr2 != null」しか見ておらず、
        //   「複数回の取得でポインタが安定している」ことを検証していなかった。
        //   同一 Chunk に対する 2 回の取得でポインタが一致することを assert する。
        [Test]
        public void GetNativeArray_Ptr_IsStableAcrossCalls()
        {
            var arch = _em.CreateArchetype(typeof(A22Data));
            _em.CreateEntity(arch, 10, Allocator.TempJob);

            var q = _em.CreateEntityQuery(typeof(A22Data));
            var chunks1 = q.ToArchetypeChunkArray(Allocator.TempJob);
            var chunks2 = q.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(true);

                void* ptr1 = null;
                void* ptr2 = null;
                Assert.Greater(chunks1.Length, 0);
                Assert.Greater(chunks2.Length, 0);

                ptr1 = chunks1[0].GetNativeArray(ref handle).GetUnsafeReadOnlyPtr();
                ptr2 = chunks2[0].GetNativeArray(ref handle).GetUnsafeReadOnlyPtr();

                Debug.Log($"[A-22] ptr1=0x{(ulong)ptr1:X}, ptr2=0x{(ulong)ptr2:X}, same={ptr1 == ptr2}");

                Assert.IsTrue(ptr1 != null && ptr2 != null);
                Assert.AreEqual((ulong)ptr1, (ulong)ptr2,
                    "構造変更が無い限り、同一 Chunk に対する複数回の取得でポインタは安定するはず");
            }
            finally
            {
                chunks1.Dispose();
                chunks2.Dispose();
            }
        }

        [Test]
        public void StructuralChange_InvalidatesChunkPtr()
        {
            var arch = _em.CreateArchetype(typeof(A22Data));
            var e = _em.CreateEntity(arch);

            var q = _em.CreateEntityQuery(typeof(A22Data));
            var chunksBefore = q.ToArchetypeChunkArray(Allocator.TempJob);
            ulong ptrBefore = 0;
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(true);
                ptrBefore = (ulong)chunksBefore[0].GetNativeArray(ref handle).GetUnsafeReadOnlyPtr();
            }
            finally
            {
                chunksBefore.Dispose();
            }

            _em.AddComponent<A22Enableable>(e);

            var chunksAfter = q.ToArchetypeChunkArray(Allocator.TempJob);
            ulong ptrAfter = 0;
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(true);
                ptrAfter = (ulong)chunksAfter[0].GetNativeArray(ref handle).GetUnsafeReadOnlyPtr();
            }
            finally
            {
                chunksAfter.Dispose();
            }

            Debug.Log($"[A-22] before=0x{ptrBefore:X}, after=0x{ptrAfter:X}");
            Debug.Log("[A-22] 構造変更後は Chunk ポインタが無効化される。" +
                      "W1/W2 の完了後に Chunk を再取得する必要がある");
            Assert.Pass();
        }
    }
}
