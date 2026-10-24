using System;
using System.Collections.Generic;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// CollectMatches で Entity.Null と既破棄 Entity を意図的に返すカスタムハンドラ。
    /// 現実にはユーザー実装のバグとして発生し得るシナリオを再現する。
    /// </summary>
    internal sealed class NullReturningHandler : SubSceneDestroyedIdentityHandler
    {
        private static readonly ulong s_typeHash =
            SubSceneTombstoneRegistry.ComputeTypeHash(typeof(NullReturningHandler).FullName);

        private readonly byte[][] _keys;

        public NullReturningHandler(byte[][] keys)
        {
            _keys = keys ?? Array.Empty<byte[]>();
        }

        public override Type IdentityType => typeof(Health);
        public override string FullTypeName => typeof(NullReturningHandler).FullName;
        public override int KeyStride => 4;
        internal override ulong TypeHash => s_typeHash;

        public override IReadOnlyList<byte[]> Snapshot(World world) => _keys;

        public override void RestoreToRegistry(
            EntityManager em, Entity singleton, IReadOnlyList<byte[]> keys)
        {
        }

        public override void CollectMatches(
            EntityManager em, Entity singleton, NativeList<Entity> output)
        {
            output.Add(Entity.Null);

            var destroyed = em.CreateEntity();
            em.DestroyEntity(destroyed);
            output.Add(destroyed);
        }
    }

    [TestFixture]
    public sealed class DestroyTargetsRobustnessTests
    {
        private const string WorldName = "DestroyTargetsRobustnessWorld";
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World(WorldName);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            _world = null;
        }

        private static SubSceneDefinition MakeSubSceneDef(NullReturningHandler handler)
        {
            return new SubSceneDefinition(
                registerSceneLoadState: false,
                registerDestroyedEntities: true,
                hasSceneRestoreCallback: false,
                withSceneStateRollback: false,
                contentVerification: ContentVerificationMode.None,
                streamingConsistency: StreamingConsistencyMode.TimeBased,
                tombstoneCapacity: 0,
                sceneStateProvider: null,
                sceneRestoreCallback: null,
                prefabIdentityProvider: null,
                destroyedHandlers: new SubSceneDestroyedIdentityHandler[] { handler },
                prefabMigrations: Array.Empty<PrefabMigrationStepBase>());
        }

        private static SaveSystem CreateSystemWithBrokenHandler(byte[][] keys)
        {
            var handler = new NullReturningHandler(keys);
            var subSceneDef = MakeSubSceneDef(handler);

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: WorldName);
            def.SubScene = subSceneDef;

            return new SaveSystem(def);
        }

        [Test]
        public void DestroyTargets_MixedWithNull_StillSucceeds()
        {
            // Arrange: 壊れたハンドラを含む SaveSystem
            var sys = CreateSystemWithBrokenHandler(new[]
            {
                new byte[] { 1, 0, 0, 0 },
            });

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            _world.Dispose();
            _world = new World(WorldName);

            // 削除対象になる余分な Entity を作る
            var extra = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra,
                new Health { Current = 999, Max = 999 });

            // Act: Null と既破棄 Entity が _destroyTargets に混入する状況で Deserialize
            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();

            // Assert: 修正2の em.Exists フィルタにより、Null/破棄済みは除外され成功する
            Assert.IsTrue(load.IsSuccess,
                $"Deserialize should succeed even when _destroyTargets contains " +
                $"Entity.Null or already-destroyed entities; got {load.ErrorCode}");

            // extra は正常に破壊されている
            Assert.IsFalse(_world.EntityManager.Exists(extra),
                "The valid destroy target (extra) should have been destroyed");
        }

        [Test]
        public void DestroyTargets_MultipleNulls_StillSucceeds()
        {
            // 複数のキーを持つハンドラ（P6_5 で CollectMatches が複数回呼ばれる）
            var sys = CreateSystemWithBrokenHandler(new[]
            {
                new byte[] { 1, 0, 0, 0 },
                new byte[] { 2, 0, 0, 0 },
                new byte[] { 3, 0, 0, 0 },
            });

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World(WorldName);

            var extra = _world.EntityManager.CreateEntity();
            _world.EntityManager.AddComponentData(extra,
                new Health { Current = 777, Max = 777 });

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByScope)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess,
                $"Deserialize should succeed; got {load.ErrorCode}");
            Assert.IsFalse(_world.EntityManager.Exists(extra));
        }

        [Test]
        public void DestroyTargets_OnlyInvalidEntities_SucceedsWithoutDestroying()
        {
            // _destroyTargets がすべて Null / 既破棄 (DeletionPolicy.ByScope でも
            // 破壊すべき有効 Entity が存在しない状況)
            var sys = CreateSystemWithBrokenHandler(new[]
            {
                new byte[] { 1, 0, 0, 0 },
            });

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 10, Max = 20 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World(WorldName);

            // 空のワールドに Deserialize（余分な Entity なし）
            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess,
                $"Load should succeed with no valid destroy targets; got {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            Assert.AreEqual(1, q.CalculateEntityCount(),
                "The saved Health entity should have been restored");
        }
    }
}
