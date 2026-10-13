using NUnit.Framework;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A06Enableable : IComponentData, IEnableableComponent { public int Value; }
    public struct A06Plain : IComponentData { public int Value; }

    [BurstCompile(CompileSynchronously = true)]
    struct A06EnableableJob : IJobChunk
    {
        public ComponentTypeHandle<A06Enableable> Handle;
        public NativeArray<int> TotalCount;
        public NativeArray<int> EnabledCount;
        public NativeArray<int> UseMaskFlags;
        public NativeArray<int> WasManaged;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            TotalCount[0] += chunk.Count;
            if (useEnabledMask) UseMaskFlags[0] = 1;

            var enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while (enumerator.NextEntityIndex(out int _))
                EnabledCount[0]++;

            WasManaged[0] = BurstProbe.IsManaged() ? 1 : 0;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct A06PlainJob : IJobChunk
    {
        public ComponentTypeHandle<A06Plain> Handle;
        public NativeArray<int> TotalCount;
        public NativeArray<int> UseMaskFlags;
        public NativeArray<int> WasManaged;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            TotalCount[0] += chunk.Count;
            if (useEnabledMask) UseMaskFlags[0] = 1;
            WasManaged[0] = BurstProbe.IsManaged() ? 1 : 0;
        }
    }

    public class A06_JobChunkEnableableTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A06");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void JobChunk_EnableableComponent_DefaultQuery_UseMaskTrue()
        {
            var arch = _em.CreateArchetype(typeof(A06Enableable));
            var es = _em.CreateEntity(arch, 20, Allocator.TempJob);
            _em.SetComponentEnabled<A06Enableable>(es[3], false);
            _em.SetComponentEnabled<A06Enableable>(es[7], false);

            var query = _em.CreateEntityQuery(typeof(A06Enableable));

            var total = new NativeArray<int>(1, Allocator.TempJob);
            var enabled = new NativeArray<int>(1, Allocator.TempJob);
            var useMask = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                var job = new A06EnableableJob
                {
                    Handle = _em.GetComponentTypeHandle<A06Enableable>(false),
                    TotalCount = total,
                    EnabledCount = enabled,
                    UseMaskFlags = useMask,
                    WasManaged = wm,
                };
                job.Schedule(query, default).Complete();

                Debug.Log($"[A-06] Default query: total={total[0]}, enabled={enabled[0]}, " +
                          $"useEnabledMask={useMask[0] == 1}, managed={wm[0] == 1}");

                Assert.AreEqual(20, total[0]);
                Assert.AreEqual(1, useMask[0], "デフォルトクエリで useEnabledMask=true にならない");
                Assert.AreEqual(18, enabled[0], "chunkEnabledMask が無効 2 件を反映していない");

                if (wm[0] == 1) Assert.Inconclusive("Burst で実行されていない");
            }
            finally
            {
                total.Dispose();
                enabled.Dispose();
                useMask.Dispose();
                wm.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void JobChunk_EnableableComponent_IgnoreQuery_UseMaskFalse()
        {
            var arch = _em.CreateArchetype(typeof(A06Enableable));
            var es = _em.CreateEntity(arch, 20, Allocator.TempJob);
            _em.SetComponentEnabled<A06Enableable>(es[3], false);
            _em.SetComponentEnabled<A06Enableable>(es[7], false);

            var query = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<A06Enableable>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            var total = new NativeArray<int>(1, Allocator.TempJob);
            var enabled = new NativeArray<int>(1, Allocator.TempJob);
            var useMask = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                var job = new A06EnableableJob
                {
                    Handle = _em.GetComponentTypeHandle<A06Enableable>(false),
                    TotalCount = total,
                    EnabledCount = enabled,
                    UseMaskFlags = useMask,
                    WasManaged = wm,
                };
                job.Schedule(query, default).Complete();

                Debug.Log($"[A-06] Ignore query: total={total[0]}, enabled={enabled[0]}, " +
                          $"useEnabledMask={useMask[0] == 1}");

                Assert.AreEqual(20, total[0]);
                Assert.AreEqual(0, useMask[0],
                    "IgnoreComponentEnabledState クエリで useEnabledMask=true になっている");
                Assert.AreEqual(20, enabled[0],
                    "マスクなしで全 20 件が処理されるはず");

                if (wm[0] == 1) Assert.Inconclusive("Burst で実行されていない");
            }
            finally
            {
                total.Dispose();
                enabled.Dispose();
                useMask.Dispose();
                wm.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void JobChunk_NonEnableable_UseMaskFalse()
        {
            var arch = _em.CreateArchetype(typeof(A06Plain));
            var es = _em.CreateEntity(arch, 15, Allocator.TempJob);
            var query = _em.CreateEntityQuery(typeof(A06Plain));

            var total = new NativeArray<int>(1, Allocator.TempJob);
            var useMask = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                new A06PlainJob
                {
                    Handle = _em.GetComponentTypeHandle<A06Plain>(false),
                    TotalCount = total,
                    UseMaskFlags = useMask,
                    WasManaged = wm,
                }.Schedule(query, default).Complete();

                Debug.Log($"[A-06] Non-Enableable: total={total[0]}, useEnabledMask={useMask[0] == 1}");

                Assert.AreEqual(15, total[0]);
                Assert.AreEqual(0, useMask[0], "非 Enableable なのに useEnabledMask=true");
            }
            finally
            {
                total.Dispose();
                useMask.Dispose();
                wm.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void JobChunk_NewSignature_WorksWithPlainType()
        {
            var arch = _em.CreateArchetype(typeof(A06Plain));
            var es = _em.CreateEntity(arch, 5, Allocator.TempJob);
            var query = _em.CreateEntityQuery(typeof(A06Plain));

            var count = new NativeArray<int>(1, Allocator.TempJob);
            var useMask = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new A06PlainJob
                {
                    Handle = _em.GetComponentTypeHandle<A06Plain>(false),
                    TotalCount = count,
                    UseMaskFlags = useMask,
                    WasManaged = wm,
                }.Schedule(query, default).Complete();

                Assert.AreEqual(5, count[0]);
                Debug.Log("[A-06] 新シグネチャ（chunkEnabledMask 付き）で正常実行");
            }
            finally
            {
                count.Dispose();
                useMask.Dispose();
                wm.Dispose();
                es.Dispose();
            }
        }

        [Test]
        public void JobChunk_ChunkEntityEnumerator_DefaultQuery_OnlyEnabled()
        {
            var arch = _em.CreateArchetype(typeof(A06Enableable));
            var es = _em.CreateEntity(arch, 30, Allocator.TempJob);
            for (int i = 0; i < 5; i++)
                _em.SetComponentEnabled<A06Enableable>(es[i], false);

            var query = _em.CreateEntityQuery(typeof(A06Enableable));

            var total = new NativeArray<int>(1, Allocator.TempJob);
            var enabled = new NativeArray<int>(1, Allocator.TempJob);
            var useMask = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new A06EnableableJob
                {
                    Handle = _em.GetComponentTypeHandle<A06Enableable>(false),
                    TotalCount = total,
                    EnabledCount = enabled,
                    UseMaskFlags = useMask,
                    WasManaged = wm,
                }.Schedule(query, default).Complete();

                Debug.Log($"[A-06] ChunkEntityEnumerator: total={total[0]}, enabled={enabled[0]}, " +
                          $"useEnabledMask={useMask[0] == 1}");
                Assert.AreEqual(30, total[0]);
                Assert.AreEqual(25, enabled[0],
                    "ChunkEntityEnumerator が有効 25 件のみを列挙していない");
            }
            finally
            {
                total.Dispose();
                enabled.Dispose();
                useMask.Dispose();
                wm.Dispose();
                es.Dispose();
            }
        }
    }
}
