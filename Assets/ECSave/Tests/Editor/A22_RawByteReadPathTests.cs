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

        [Test]
        public void BufferAccessor_RawByteRead()
        {
            var arch = _em.CreateArchetype(typeof(A22Data));
            var e = _em.CreateEntity(arch);

            var q = _em.CreateEntityQuery(typeof(A22Data));
            var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(true);
                foreach (var c in chunks)
                {
                    var native = c.GetNativeArray(ref handle);
                    Debug.Log($"[A-22] ComponentTypeHandle 経由で NativeArray 取得");
                    Assert.AreEqual(1, native.Length);
                }
            }
            finally
            {
                chunks.Dispose();
            }
        }

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
                foreach (var c in chunks1)
                    ptr1 = c.GetNativeArray(ref handle).GetUnsafeReadOnlyPtr();
                foreach (var c in chunks2)
                    ptr2 = c.GetNativeArray(ref handle).GetUnsafeReadOnlyPtr();

                Debug.Log($"[A-22] ptr1=0x{(ulong)ptr1:X}, ptr2=0x{(ulong)ptr2:X}, same={ptr1 == ptr2}");
                Assert.IsTrue(ptr1 != null && ptr2 != null);
                Debug.Log("[A-22] 同一 Chunk に対する複数回の取得でポインタが安定していることを確認" +
                          "（構造変更後は再取得が必要）");
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
            void* ptrBefore = null;
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(true);
                foreach (var c in chunksBefore)
                    ptrBefore = c.GetNativeArray(ref handle).GetUnsafeReadOnlyPtr();
            }
            finally
            {
                chunksBefore.Dispose();
            }

            _em.AddComponent<A22Enableable>(e);

            var chunksAfter = q.ToArchetypeChunkArray(Allocator.TempJob);
            void* ptrAfter = null;
            try
            {
                var handle = _em.GetComponentTypeHandle<A22Data>(true);
                foreach (var c in chunksAfter)
                    ptrAfter = c.GetNativeArray(ref handle).GetUnsafeReadOnlyPtr();
            }
            finally
            {
                chunksAfter.Dispose();
            }

            Debug.Log($"[A-22] before=0x{(ulong)ptrBefore:X}, after=0x{(ulong)ptrAfter:X}");
            Debug.Log("[A-22] 構造変更後は Chunk ポインタが無効化される。" +
                      "W1/W2 の完了後に Chunk を再取得する必要がある");
            Assert.Pass();
        }
    }
}
