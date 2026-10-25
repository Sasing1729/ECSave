using System;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    [TestFixture]
    public sealed class RestorationScopeValidationTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("RestorationScopeValidationWorld");
            _world.CreateSystemManaged<SaveRuntimeSystem>();

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "RestorationScopeValidationWorld");
            _system = new SaveSystem(def);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            _world = null;
        }

        // ─────────────────────────────────────────────
        // DeserializeAsync: 無効な Custom scope → ArgumentException
        // ─────────────────────────────────────────────

        [Test]
        public void DeserializeAsync_CustomScopeFromDifferentWorld_ThrowsArgumentException()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));

            Assert.Throws<ArgumentException>(() =>
                _system.Deserialize(_world, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void DeserializeAsync_CustomScopeDisposedQuery_ThrowsArgumentException()
        {
            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            q.Dispose();

            Assert.Throws<ArgumentException>(() =>
                _system.Deserialize(_world, new byte[] { 1 },
                    RestorationScope.Custom(q), DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void DeserializeAsync_CustomScopeDefaultQuery_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _system.Deserialize(_world, new byte[] { 1 },
                    RestorationScope.Custom(default), DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void DeserializeAsync_CustomScopeFromDifferentWorld_MessageMentionsRestorationScopeCustom()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));

            var ex = Assert.Throws<ArgumentException>(() =>
                _system.Deserialize(_world, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None)
                    .GetAwaiter().GetResult());

            StringAssert.Contains("RestorationScope.Custom", ex.Message);
            Assert.AreEqual("restorationScope", ex.ParamName);
        }

        // ─────────────────────────────────────────────
        // DeserializeAsync: 有効な scope は検証を通過して後続処理へ
        // ─────────────────────────────────────────────

        [Test]
        public void DeserializeAsync_CustomScopeSameWorld_NoArgumentException()
        {
            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));

            var result = _system.Deserialize(_world, new byte[] { 1, 2, 3, 4 },
                RestorationScope.Custom(q), DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, result.ErrorCode);
        }

        [Test]
        public void DeserializeAsync_EntireWorld_NoValidation()
        {
            var result = _system.Deserialize(_world, new byte[] { 1, 2, 3, 4 },
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, result.ErrorCode);
        }

        // ─────────────────────────────────────────────
        // 検証順序: 引数不正がキャンセルより先
        // ─────────────────────────────────────────────

        [Test]
        public void DeserializeAsync_InvalidScopeAndCancelled_ThrowsArgumentException_NotCancelled()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<ArgumentException>(() =>
                _system.Deserialize(_world, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None, cts.Token)
                    .GetAwaiter().GetResult());
        }

        // ─────────────────────────────────────────────
        // 検証順序: null / disposed チェックが先
        // ─────────────────────────────────────────────

        [Test]
        public void DeserializeAsync_NullWorld_ThrowsArgumentNullException_NotArgumentException()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));

            Assert.Throws<ArgumentNullException>(() =>
                _system.Deserialize(null, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void DeserializeAsync_NullData_ThrowsArgumentNullException_NotArgumentException()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));

            Assert.Throws<ArgumentNullException>(() =>
                _system.Deserialize(_world, null,
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void DeserializeAsync_DisposedWorld_ThrowsObjectDisposedException_NotArgumentException()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));

            var deadWorld = new World("DeadWorld");
            deadWorld.Dispose();

            Assert.Throws<ObjectDisposedException>(() =>
                _system.Deserialize(deadWorld, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        // ─────────────────────────────────────────────
        // DeserializeAsyncNonBlocking: 無効な Custom scope → ArgumentException
        // ─────────────────────────────────────────────

        [Test]
        public void DeserializeAsyncNonBlocking_CustomScopeFromDifferentWorld_ThrowsArgumentException()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));

            Assert.Throws<ArgumentException>(() =>
                _system.DeserializeAsyncNonBlocking(_world, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None));
        }

        [Test]
        public void DeserializeAsyncNonBlocking_CustomScopeDisposedQuery_ThrowsArgumentException()
        {
            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            q.Dispose();

            Assert.Throws<ArgumentException>(() =>
                _system.DeserializeAsyncNonBlocking(_world, new byte[] { 1 },
                    RestorationScope.Custom(q), DeletionPolicy.None));
        }

        [Test]
        public void DeserializeAsyncNonBlocking_CustomScopeDefaultQuery_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                _system.DeserializeAsyncNonBlocking(_world, new byte[] { 1 },
                    RestorationScope.Custom(default), DeletionPolicy.None));
        }

        [Test]
        public void DeserializeAsyncNonBlocking_InvalidScopeAndCancelled_ThrowsArgumentException()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<ArgumentException>(() =>
                _system.DeserializeAsyncNonBlocking(_world, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None, cts.Token));
        }

        [Test]
        public void DeserializeAsyncNonBlocking_EntireWorld_NoArgumentExceptionThrown()
        {
            // PlayerLoop が tick しない [Test] ではタスクは完了しないが、
            // ArgumentException が同期 throw されないことだけ検証する
            var task = _system.DeserializeAsyncNonBlocking(_world, new byte[] { 1, 2, 3, 4 },
                RestorationScope.EntireWorld, DeletionPolicy.None);

            Assert.IsNotNull(task);
        }

        // ─────────────────────────────────────────────
        // ガード解放: 検証失敗時にロックが保持されない
        // ─────────────────────────────────────────────

        [Test]
        public void DeserializeAsync_InvalidScope_GuardNotAcquired()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));

            try
            {
                _system.Deserialize(_world, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None)
                    .GetAwaiter().GetResult();
            }
            catch (ArgumentException)
            {
            }

            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            Assert.IsFalse(sys.IsBusy, "Guard must not be held after validation failure");

            // 後続の有効な呼び出しが通ることも確認
            var result = _system.Deserialize(_world, new byte[] { 1, 2, 3, 4 },
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, result.ErrorCode);
        }

        [Test]
        public void DeserializeAsyncNonBlocking_InvalidScope_GuardNotAcquired()
        {
            using var otherWorld = new World("OtherWorld");
            var otherQuery = otherWorld.EntityManager.CreateEntityQuery(typeof(Health));

            try
            {
                _system.DeserializeAsyncNonBlocking(_world, new byte[] { 1 },
                    RestorationScope.Custom(otherQuery), DeletionPolicy.None);
            }
            catch (ArgumentException)
            {
            }

            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            Assert.IsFalse(sys.IsBusy, "Guard must not be held after validation failure");
        }

        // ─────────────────────────────────────────────
        // SerializeAsync: RestorationScope 未使用のため検証なし
        // ─────────────────────────────────────────────

        [Test]
        public void SerializeAsync_DoesNotAcceptRestorationScope()
        {
            // SerializeAsync は RestorationScope を受け取らない設計。
            // 別 World のクエリを渡す経路自体が存在しないことを「コンパイル時の契約」として記録する。
            // このテストは「SerializeAsync が RestorationScope 引数を持たない」ことの
            // リフレクションによる確認で、設計変更時に検出するための回帰テスト。
            var methods = typeof(SaveSystem).GetMethods();
            foreach (var m in methods)
            {
                if (m.Name != "SerializeAsync" && m.Name != "SerializeAsyncNonBlocking") continue;
                foreach (var p in m.GetParameters())
                {
                    Assert.AreNotEqual(typeof(RestorationScope), p.ParameterType,
                        $"{m.Name} should not accept RestorationScope");
                }
            }
        }
    }
}
