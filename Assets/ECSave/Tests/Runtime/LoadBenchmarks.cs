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
    /// Load (Deserialize) のベンチマーク。10 万 Entity の Health コンポーネント。
    /// 総時間とフェーズ別内訳を計測する。
    /// </summary>
    public unsafe class LoadBenchmarks
    {
        private World _world;
        private SaveSystem _system;
        private byte[] _saveData;
        private const int EntityCount = 100_000;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("LoadBenchmarkWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "LoadBenchmarkWorld");
            _system = new SaveSystem(definition);

            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(Health));
            using var entities = em.CreateEntity(archetype, EntityCount, Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
                em.SetComponentData(entities[i], new Health { Current = i, Max = 1000 });

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);
            _saveData = save.Data;

            _world.Dispose();
            _world = new World("LoadBenchmarkWorld");
        }

        [TearDown]
        public void TearDown()
        {
            DeserializeDiag.Enabled = false;
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void FullDeserialize_10万Entity()
        {
            // ウォームアップ（2回）
            for (int w = 0; w < 2; w++)
            {
                var r = _system.Deserialize(_world, _saveData,
                    RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(r.IsSuccess, $"warmup: {r.ErrorCode}");
            }

            const int iterations = 5;
            var sw = new Stopwatch();
            double totalMs = 0;

            for (int it = 0; it < iterations; it++)
            {
                sw.Restart();
                var result = _system.Deserialize(_world, _saveData,
                    RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                    .GetAwaiter().GetResult();
                sw.Stop();

                Assert.IsTrue(result.IsSuccess);
                Assert.AreEqual(EntityCount, result.EntityCount);
                totalMs += sw.Elapsed.TotalMilliseconds;
            }

            double avgMs = totalMs / iterations;
            UnityEngine.Debug.Log(
                $"[LoadBenchmark] Full Deserialize ({EntityCount} entities): avg={avgMs:F3} ms/回 " +
                $"(target: 5000ms)");

            if (avgMs > 5000.0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[LoadBenchmark] Deserialize exceeds 5000ms target: {avgMs:F3} ms");
            }
        }

        [Test]
        public void Deserialize_InternalBreakdown()
        {
            // ウォームアップ
            for (int w = 0; w < 2; w++)
            {
                _system.Deserialize(_world, _saveData,
                    RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                    .GetAwaiter().GetResult();
            }

            const int iterations = 10;

            DeserializeDiag.Enabled = true;
            DeserializeDiag.Reset();

            for (int it = 0; it < iterations; it++)
            {
                var result = _system.Deserialize(_world, _saveData,
                    RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(result.IsSuccess);
            }

            DeserializeDiag.Dump(
                $"DeserializeOperation internal (Health x {EntityCount})", iterations);
            DeserializeDiag.Enabled = false;
        }

        [Test]
        public void FirstLoad_EmptyWorld()
        {
            // 空 World への初回ロード（新規生成 10 万件）の計測
            const int iterations = 5;
            var sw = new Stopwatch();
            double totalMs = 0;

            for (int it = 0; it < iterations; it++)
            {
                _world.Dispose();
                _world = new World("LoadBenchmarkWorld");

                sw.Restart();
                var result = _system.Deserialize(_world, _saveData,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                sw.Stop();

                Assert.IsTrue(result.IsSuccess, $"iteration {it}: {result.ErrorCode}");
                Assert.AreEqual(EntityCount, result.EntityCount);
                totalMs += sw.Elapsed.TotalMilliseconds;
            }

            double avgMs = totalMs / iterations;
            UnityEngine.Debug.Log(
                $"[LoadBenchmark] First Deserialize to empty world ({EntityCount} entities): " +
                $"avg={avgMs:F3} ms/回");

            // 1 回目の結果も残す（ウォームアップなし）
            _world.Dispose();
            _world = new World("LoadBenchmarkWorld");

            sw.Restart();
            var first = _system.Deserialize(_world, _saveData,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            sw.Stop();

            UnityEngine.Debug.Log(
                $"[LoadBenchmark] Cold first Deserialize: {sw.Elapsed.TotalMilliseconds:F3} ms " +
                $"(success={first.IsSuccess})");
        }
    }
}
