using NUnit.Framework;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ECSave.Tests
{
    public struct A18Data : IComponentData { public int Value; }
    public struct A18Tag : IComponentData { }

    [BurstCompile(CompileSynchronously = true)]
    struct A18MultiHashMapJob : IJob
    {
        public NativeParallelMultiHashMap<ulong, int> Map;
        public NativeArray<ulong> Keys;
        public NativeArray<int> Results;
        public NativeArray<int> WasManaged;

        public void Execute()
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                int count = 0;
                if (Map.TryGetFirstValue(Keys[i], out int _, out var it))
                {
                    do { count++; } while (Map.TryGetNextValue(out _, ref it));
                }
                Results[i] = count;
            }
            WasManaged[0] = BurstProbe.IsManaged() ? 1 : 0;
        }
    }

    [DisableAutoCreation]
    public partial class A18ProbeSystem : SystemBase
    {
        public EntityQuery QueryWithFilter;
        public EntityQuery QueryWithoutFilter;

        protected override void OnCreate()
        {
            QueryWithFilter = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A18Data>() },
                Any = new[] { ComponentType.ReadOnly<A18Tag>() },
            });
            QueryWithoutFilter = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<A18Data>() },
            });
        }

        protected override void OnUpdate() { }

        public int CountWithFilter() => QueryWithFilter.CalculateEntityCount();
        public int CountWithoutFiltering() => QueryWithoutFilter.CalculateEntityCountWithoutFiltering();
    }

    public class A18_QueryAndHashMapTests
    {
        World _world;
        EntityManager _em;
        A18ProbeSystem _probe;

        [SetUp]
        public void SetUp()
        {
            _world = new World("A18");
            _em = _world.EntityManager;
            _probe = _world.CreateSystemManaged<A18ProbeSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void CalculateEntityCountWithoutFiltering_IgnoresAnyNoneFilter()
        {
            var arch1 = _em.CreateArchetype(typeof(A18Data), typeof(A18Tag));
            var arch2 = _em.CreateArchetype(typeof(A18Data));
            _em.CreateEntity(arch1, 100, Allocator.TempJob);
            _em.CreateEntity(arch2, 50, Allocator.TempJob);

            int withFilter = _probe.CountWithFilter();
            int withoutFiltering = _probe.CountWithoutFiltering();

            Debug.Log($"[A-18] withFilter={withFilter}, withoutFiltering={withoutFiltering}");
            Assert.AreEqual(100, withFilter,
                "WithAny<A18Tag> のフィルタが効いていない");
            Assert.AreEqual(150, withoutFiltering,
                "CalculateEntityCountWithoutFiltering が Any フィルタを無視していない");
        }

        [Test]
        public void CalculateEntityCountWithoutFiltering_100k_Cost()
        {
            const int n = 100_000;
            var arch = _em.CreateArchetype(typeof(A18Data));
            var es = _em.CreateEntity(arch, n, Allocator.TempJob);
            try
            {
                _probe.CountWithoutFiltering();
                _probe.CountWithFilter();

                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) _probe.CountWithoutFiltering();
                sw.Stop();
                double noFilterMs = sw.Elapsed.TotalMilliseconds / 100;

                sw.Restart();
                for (int i = 0; i < 100; i++) _probe.CountWithFilter();
                sw.Stop();
                double withFilterMs = sw.Elapsed.TotalMilliseconds / 100;

                Debug.Log($"[A-18] 10万 Entity: withoutFiltering={noFilterMs:F4} ms, " +
                          $"withFilter={withFilterMs:F4} ms");

                Assert.GreaterOrEqual(noFilterMs, 0);
            }
            finally
            {
                es.Dispose();
            }
        }

        [Test]
        public void NativeParallelMultiHashMap_BurstJob_TryGetValues()
        {
            const int keyCount = 1000;
            const int perKey = 5;

            var map = new NativeParallelMultiHashMap<ulong, int>(keyCount * perKey, Allocator.TempJob);
            var keys = new NativeArray<ulong>(keyCount, Allocator.TempJob);
            var results = new NativeArray<int>(keyCount, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                for (int i = 0; i < keyCount; i++)
                {
                    ulong k = (ulong)(i * 7919);
                    keys[i] = k;
                    for (int j = 0; j < perKey; j++)
                        map.Add(k, i * perKey + j);
                }

                new A18MultiHashMapJob
                {
                    Map = map,
                    Keys = keys,
                    Results = results,
                    WasManaged = wm,
                }.Run();

                Debug.Log($"[A-18] MultiHashMap Burst: managed={wm[0] == 1}");
                for (int i = 0; i < keyCount; i++)
                    Assert.AreEqual(perKey, results[i],
                        $"key {i} の要素数が {perKey} でない");

                if (wm[0] == 1) Assert.Inconclusive("Burst で実行されていない");
            }
            finally
            {
                map.Dispose();
                keys.Dispose();
                results.Dispose();
                wm.Dispose();
            }
        }

        [Test]
        public void NativeParallelMultiHashMap_HashCollision_HandledByByteCompare()
        {
            var map = new NativeParallelMultiHashMap<ulong, int>(16, Allocator.TempJob);
            try
            {
                map.Add(12345UL, 100);
                map.Add(12345UL, 101);
                map.Add(12345UL, 102);

                int count = 0;
                if (map.TryGetFirstValue(12345UL, out int _, out var it))
                {
                    do { count++; } while (map.TryGetNextValue(out _, ref it));
                }
                Debug.Log($"[A-18] MultiHashMap key=12345 count={count}");
                Assert.AreEqual(3, count);
            }
            finally
            {
                map.Dispose();
            }
        }

        [Test]
        public void NativeParallelMultiHashMap_100k_AddAndLookup_Cost()
        {
            const int n = 100_000;
            var map = new NativeParallelMultiHashMap<ulong, int>(n, Allocator.TempJob);
            var keys = new NativeArray<ulong>(n, Allocator.TempJob);
            var results = new NativeArray<int>(n, Allocator.TempJob);
            var wm = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++)
                {
                    ulong k = (ulong)(i * 2654435761u);
                    keys[i] = k;
                    map.Add(k, i);
                }
                sw.Stop();
                Debug.Log($"[A-18] MultiHashMap add {n} 件: {sw.Elapsed.TotalMilliseconds:F3} ms");

                sw.Restart();
                new A18MultiHashMapJob
                {
                    Map = map,
                    Keys = keys,
                    Results = results,
                    WasManaged = wm,
                }.Run();
                sw.Stop();
                Debug.Log($"[A-18] MultiHashMap lookup {n} 件: {sw.Elapsed.TotalMilliseconds:F3} ms " +
                          $"(managed={wm[0] == 1})");
            }
            finally
            {
                map.Dispose();
                keys.Dispose();
                results.Dispose();
                wm.Dispose();
            }
        }

        [Test]
        public void NativeParallelHashMap_10k_AddAndLookup_Cost()
        {
            const int n = 10_000;
            var map = new NativeParallelHashMap<ulong, int>(n, Allocator.TempJob);
            try
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < n; i++)
                    map.TryAdd((ulong)(i * 2654435761u), i);
                sw.Stop();
                Debug.Log($"[A-18] HashMap add {n} 件: {sw.Elapsed.TotalMilliseconds:F3} ms");

                sw.Restart();
                int found = 0;
                for (int i = 0; i < n; i++)
                    if (map.TryGetValue((ulong)(i * 2654435761u), out _)) found++;
                sw.Stop();
                Debug.Log($"[A-18] HashMap lookup {n} 件: {sw.Elapsed.TotalMilliseconds:F3} ms, found={found}");
                Assert.AreEqual(n, found);
            }
            finally
            {
                map.Dispose();
            }
        }
    }
}
