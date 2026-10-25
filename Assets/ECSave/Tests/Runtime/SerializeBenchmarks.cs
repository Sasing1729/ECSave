using System.Diagnostics;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// Phase 5f-0：確定区間の計測（テスト計画書 §8.2 項目 5）。
    /// 目標（T-01）：確定区間全体で 2ms 以内。
    /// 結果次第で Phase 5f の範囲を決定する。
    /// </summary>
    public class SerializeBenchmarks
    {
        private World _world;
        private SaveSystem _system;
        private const int EntityCount = 100_000;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("BenchmarkWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "BenchmarkWorld");
            _system = new SaveSystem(definition);

            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(Health));
            using var entities = em.CreateEntity(archetype, EntityCount, Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                em.SetComponentData(entities[i], new Health { Current = i, Max = 1000 });
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public unsafe void CapturePhase_10万Entity()
        {
            var em = _world.EntityManager;
            var query = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Health>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });

            // ウォームアップ（JIT、Burst 初回）
            for (int w = 0; w < 3; w++)
            {
                var warmup = new SnapshotBuffer(EntityCount, sizeof(Health), Allocator.TempJob);
                try
                {
                    SnapshotCapture.CaptureChunksPlain<Health>(query, em, ref warmup);
                }
                finally
                {
                    warmup.Dispose();
                }
            }

            // 計測（10回）
            const int iterations = 10;
            var sw = new Stopwatch();
            double totalMs = 0;

            for (int it = 0; it < iterations; it++)
            {
                var buffer = new SnapshotBuffer(EntityCount, sizeof(Health), Allocator.TempJob);
                try
                {
                    sw.Restart();
                    SnapshotCapture.CaptureChunksPlain<Health>(query, em, ref buffer);
                    sw.Stop();

                    Assert.AreEqual(EntityCount, buffer.Count);
                    totalMs += sw.Elapsed.TotalMilliseconds;
                }
                finally
                {
                    buffer.Dispose();
                }
            }

            double avgMs = totalMs / iterations;
            UnityEngine.Debug.Log(
                $"[Phase 5f-0] Capture ({EntityCount} entities): avg={avgMs:F3} ms/回 " +
                $"(target: 2ms)");

            // 2ms 以内なら Passed。超過しても失敗させず、警告として記録する。
            if (avgMs > 2.0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[Phase 5f-0] Capture exceeds 2ms target: {avgMs:F3} ms. " +
                    $"ジョブ化（Phase 5f-1/5f-2）が必要。");
            }
        }

        [Test]
        public void FullSerialize_10万Entity()
        {
            // ウォームアップ
            for (int w = 0; w < 2; w++)
            {
                var r = _system.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsTrue(r.IsSuccess);
            }

            const int iterations = 5;
            var sw = new Stopwatch();
            double totalMs = 0;

            for (int it = 0; it < iterations; it++)
            {
                sw.Restart();
                var result = _system.Serialize(_world).GetAwaiter().GetResult();
                sw.Stop();

                Assert.IsTrue(result.IsSuccess);
                totalMs += sw.Elapsed.TotalMilliseconds;
            }

            double avgMs = totalMs / iterations;
            UnityEngine.Debug.Log(
                $"[Phase 5f-0] Full Serialize ({EntityCount} entities): avg={avgMs:F3} ms/回");
        }
    }
}
