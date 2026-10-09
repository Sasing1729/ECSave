using NUnit.Framework;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace ECSave.Tests
{
    public struct A08Value : IComponentData { public int Value; }
    public struct A08ChunkData : IComponentData { public int Value; }

    [BurstCompile(CompileSynchronously = true)]
    struct A08ReadAndWriteJob : IJobChunk
    {
        [ReadOnly] public EntityTypeHandle EntityHandle;
        public ComponentTypeHandle<A08ChunkData> ChunkDataHandle;
        public int WriteBase;
        public NativeArray<int> EntityCount;
        public NativeArray<int> ChunkCount;
        public NativeArray<int> WasManaged;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(EntityHandle);
            EntityCount[0] += entities.Length;
            ChunkCount[0]++;

            chunk.SetChunkComponentData(ref ChunkDataHandle,
                new A08ChunkData { Value = WriteBase + unfilteredChunkIndex });

            WasManaged[0] = BurstProbe.IsManaged() ? 1 : 0;
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct A08WriteOnlyJob : IJobChunk
    {
        public ComponentTypeHandle<A08ChunkData> ChunkDataHandle;
        public int WriteBase;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            chunk.SetChunkComponentData(ref ChunkDataHandle,
                new A08ChunkData { Value = WriteBase + unfilteredChunkIndex });
        }
    }

    [BurstCompile(CompileSynchronously = true)]
    struct A08ReadJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<A08ChunkData> ChunkDataHandle;
        public NativeArray<int> Values;
        public NativeArray<int> ChunkCount;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
                            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var data = chunk.GetChunkComponentData(ref ChunkDataHandle);
            Values[unfilteredChunkIndex] = data.Value;
            ChunkCount[0]++;
        }
    }

    public class A08_ChunkComponentJobTests
    {
        World _world;
        EntityManager _em;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A08");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        NativeArray<Entity> CreateEntitiesWithChunk(int n)
        {
            var arch = _em.CreateArchetype(
                typeof(A08Value),
                ComponentType.ChunkComponent<A08ChunkData>());
            return _em.CreateEntity(arch, n, Allocator.TempJob);
        }

        EntityQuery BuildQuery()
        {
            return _em.CreateEntityQuery(
                typeof(A08Value),
                ComponentType.ChunkComponent<A08ChunkData>());
        }

        [Test]
        public void Job_ReadsEntities_AndWritesChunkComponent()
        {
            var entities = CreateEntitiesWithChunk(50);
            var query = BuildQuery();

            var entityHandle = _em.GetEntityTypeHandle();
            var chunkRW = _em.GetComponentTypeHandle<A08ChunkData>(false);

            var ec = new NativeArray<int>(1, Allocator.TempJob);
            var cc = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                var job = new A08ReadAndWriteJob
                {
                    EntityHandle = entityHandle,
                    ChunkDataHandle = chunkRW,
                    WriteBase = 1000,
                    EntityCount = ec,
                    ChunkCount = cc,
                    WasManaged = wm,
                };
                job.Schedule(query, default).Complete();

                Debug.Log($"[A-08] entities={ec[0]}, chunks={cc[0]}, managed={wm[0] == 1} " +
                          $"(BurstCompiler.IsEnabled={BurstCompiler.IsEnabled})");

                Assert.AreEqual(50, ec[0]);
                Assert.Greater(cc[0], 0);

                var seen = new HashSet<int>();
                foreach (var e in entities)
                {
                    var d = _em.GetChunkComponentData<A08ChunkData>(e);
                    Assert.GreaterOrEqual(d.Value, 1000);
                    seen.Add(d.Value);
                }
                Debug.Log($"[A-08] distinct chunk values={seen.Count}");

                if (wm[0] == 1)
                    Assert.Inconclusive("Burst で実行されていない");
            }
            finally
            {
                entities.Dispose();
                ec.Dispose(); cc.Dispose(); wm.Dispose();
            }
        }

        [Test]
        public void Job_DependencyChain_WriteThenRead()
        {
            var entities = CreateEntitiesWithChunk(200);
            var query = BuildQuery();

            var ec = new NativeArray<int>(1, Allocator.TempJob);
            var cc = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            var values = new NativeArray<int>(64, Allocator.TempJob);
            var rc = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                var chunkRW = _em.GetComponentTypeHandle<A08ChunkData>(false);
                var writeJob = new A08ReadAndWriteJob
                {
                    EntityHandle = _em.GetEntityTypeHandle(),
                    ChunkDataHandle = chunkRW,
                    WriteBase = 500,
                    EntityCount = ec,
                    ChunkCount = cc,
                    WasManaged = wm,
                };
                var writeDeps = writeJob.Schedule(query, default);

                var chunkRO = _em.GetComponentTypeHandle<A08ChunkData>(true);
                var readJob = new A08ReadJob
                {
                    ChunkDataHandle = chunkRO,
                    Values = values,
                    ChunkCount = rc,
                };
                readJob.Schedule(query, writeDeps).Complete();

                Debug.Log($"[A-08] write chunks={cc[0]}, read chunks={rc[0]}");

                Assert.AreEqual(cc[0], rc[0]);
                for (int i = 0; i < rc[0]; i++)
                    Assert.GreaterOrEqual(values[i], 500);
            }
            finally
            {
                entities.Dispose();
                ec.Dispose(); cc.Dispose(); wm.Dispose();
                values.Dispose(); rc.Dispose();
            }
        }

        [Test]
        public void Job_Scheduled_ThenComplete_ThenMainThreadRead()
        {
            var entities = CreateEntitiesWithChunk(30);
            var query = BuildQuery();

            var ec = new NativeArray<int>(1, Allocator.TempJob);
            var cc = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                var chunkRW = _em.GetComponentTypeHandle<A08ChunkData>(false);
                var job = new A08ReadAndWriteJob
                {
                    EntityHandle = _em.GetEntityTypeHandle(),
                    ChunkDataHandle = chunkRW,
                    WriteBase = 7777,
                    EntityCount = ec,
                    ChunkCount = cc,
                    WasManaged = wm,
                };
                var handle = job.Schedule(query, default);
                handle.Complete();

                var d = _em.GetChunkComponentData<A08ChunkData>(entities[0]);
                Debug.Log($"[A-08] Complete 後の読取り: {d.Value}");
                Assert.GreaterOrEqual(d.Value, 7777);
            }
            finally
            {
                entities.Dispose();
                ec.Dispose(); cc.Dispose(); wm.Dispose();
            }
        }

        [Test]
        public void Job_ScheduleParallel_ChunkComponentWrite()
        {
            var entities = CreateEntitiesWithChunk(1000);
            var query = BuildQuery();

            try
            {
                var chunkRW = _em.GetComponentTypeHandle<A08ChunkData>(false);
                var job = new A08WriteOnlyJob
                {
                    ChunkDataHandle = chunkRW,
                    WriteBase = 100,
                };
                job.ScheduleParallel(query, default).Complete();

                Debug.Log("[A-08] ScheduleParallel 書込み完了");
                foreach (var e in entities)
                {
                    var d = _em.GetChunkComponentData<A08ChunkData>(e);
                    Assert.GreaterOrEqual(d.Value, 100);
                }
            }
            finally
            {
                entities.Dispose();
            }
        }

        [Test]
        public void Cost_100kEntities_LogOnly()
        {
            const int n = 100_000;
            var entities = CreateEntitiesWithChunk(n);
            var query = BuildQuery();

            var ec = new NativeArray<int>(1, Allocator.TempJob);
            var cc = new NativeArray<int>(1, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                var chunkRW = _em.GetComponentTypeHandle<A08ChunkData>(false);
                var job = new A08ReadAndWriteJob
                {
                    EntityHandle = _em.GetEntityTypeHandle(),
                    ChunkDataHandle = chunkRW,
                    WriteBase = 0,
                    EntityCount = ec,
                    ChunkCount = cc,
                    WasManaged = wm,
                };
                job.Schedule(query, default).Complete();

                var sw = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 10; i++)
                {
                    ec[0] = 0;
                    cc[0] = 0;
                    job.Schedule(query, default).Complete();
                }
                sw.Stop();

                Debug.Log($"[A-08] 10万 Entity: {sw.Elapsed.TotalMilliseconds / 10:F3} ms/回, " +
                          $"chunks={cc[0]}, managed={wm[0] == 1}");
                Assert.AreEqual(n, ec[0]);
            }
            finally
            {
                entities.Dispose();
                ec.Dispose(); cc.Dispose(); wm.Dispose();
            }
        }
    }
}
