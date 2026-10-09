using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Entities;

namespace ECSave.Tests.MinimalE2E
{
    /// <summary>
    /// Phase 4 最小 E2E：Health のラウンドトリップ（I-01 の最小版）。
    /// アーキテクチャ検証が目的。性能・並列性は対象外。
    /// </summary>
    public class MinimalE2ETests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();

            _world = new World("MinimalE2ETestWorld");
            var definition = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "MinimalE2ETestWorld");
            _system = new SaveSystem(definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void Health_RoundTrip_PreservesValues()
        {
            // Arrange
            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(Health));

            int count = 10;
            for (int i = 0; i < count; i++)
            {
                var e = em.CreateEntity(archetype);
                em.SetComponentData(e, new Health { Current = i * 10, Max = 100 });
            }

            // Act 1: Serialize
            var saveResult = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(saveResult.IsSuccess, $"Serialize failed: {saveResult.ErrorCode}");
            Assert.IsNotNull(saveResult.Data);
            Assert.Greater(saveResult.Data.Length, 0);

            // World を破棄して再構築（ロード検証のため）
            _world.Dispose();
            _world = new World("MinimalE2ETestWorld");

            // Act 2: Deserialize
            var loadResult = _system
                .DeserializeAsync(_world, saveResult.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsTrue(loadResult.IsSuccess, $"Deserialize failed: {loadResult.ErrorCode}");
            Assert.AreEqual(count, loadResult.EntityCount);

            // Assert: 値が復元されている
            var query = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var healths = query.ToComponentDataArray<Health>(Unity.Collections.Allocator.Temp);
            Assert.AreEqual(count, healths.Length);

            // 順序は非決定的なので、値の集合で比較する
            var actual = new System.Collections.Generic.HashSet<(int, int)>();
            for (int i = 0; i < healths.Length; i++)
                actual.Add((healths[i].Current, healths[i].Max));

            for (int i = 0; i < count; i++)
                Assert.IsTrue(actual.Contains((i * 10, 100)),
                    $"missing Health({i * 10}, 100)");
        }

        [Test]
        public void Serialize_EmptyWorld_Succeeds()
        {
            var result = _system.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess, $"ErrorCode={result.ErrorCode}");
            Assert.IsNotNull(result.Data);
            Assert.Greater(result.Data.Length, 0);
        }

        [Test]
        public void Deserialize_CorruptedData_Fails()
        {
            // 適当なゴミデータ
            var bad = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 };
            var result = _system
                .DeserializeAsync(_world, bad,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, result.ErrorCode);
        }

        [Test]
        public void Deserialize_EmptyArray_Fails()
        {
            var result = _system
                .DeserializeAsync(_world, new byte[0],
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, result.ErrorCode);
        }
    }
}
