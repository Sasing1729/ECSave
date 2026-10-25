using System.Threading;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class OperationTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("OperationTestWorld");
            _world.CreateSystemManaged<SaveRuntimeSystem>();

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "OperationTestWorld");
            _system = new SaveSystem(def);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void SerializeAsync_NoGuard_Works()
        {
            var result = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess, $"Serialize failed: {result.ErrorCode}");
        }

        [Test]
        public void SerializeAsync_GuardHeld_ReturnsConcurrentOperation()
        {
            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            Assert.IsNotNull(sys);
            Assert.IsTrue(sys.TryAcquireOperation());

            try
            {
                var result = _system.Serialize(_world).GetAwaiter().GetResult();
                Assert.IsFalse(result.IsSuccess);
                Assert.AreEqual(SaveLoadErrorCode.ConcurrentOperation, result.ErrorCode);
            }
            finally
            {
                sys.ReleaseOperation();
            }
        }

        [Test]
        public void DeserializeAsync_GuardHeld_ReturnsConcurrentOperation()
        {
            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            Assert.IsTrue(sys.TryAcquireOperation());

            try
            {
                var result = _system.Deserialize(_world, new byte[] { 1, 2, 3 },
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult();
                Assert.IsFalse(result.IsSuccess);
                Assert.AreEqual(SaveLoadErrorCode.ConcurrentOperation, result.ErrorCode);
            }
            finally
            {
                sys.ReleaseOperation();
            }
        }

        [Test]
        public void SerializeAsync_AfterRelease_Succeeds()
        {
            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            sys.TryAcquireOperation();
            sys.ReleaseOperation();

            var result = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess);
        }

        [Test]
        public void SerializeAsync_PreCancelled_ReturnsCancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = _system.Serialize(_world, cts.Token).GetAwaiter().GetResult();
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.Cancelled, result.ErrorCode);
        }

        [Test]
        public void DeserializeAsync_PreCancelled_ReturnsCancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = _system.Deserialize(_world, new byte[] { 1 },
                RestorationScope.EntireWorld, DeletionPolicy.None, cts.Token)
                .GetAwaiter().GetResult();
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.Cancelled, result.ErrorCode);
        }

        [Test]
        public void SerializeAsync_CancelledWinsOverConcurrentOperation()
        {
            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            sys.TryAcquireOperation();

            try
            {
                using var cts = new CancellationTokenSource();
                cts.Cancel();

                var result = _system.Serialize(_world, cts.Token).GetAwaiter().GetResult();
                Assert.AreEqual(SaveLoadErrorCode.Cancelled, result.ErrorCode);
            }
            finally
            {
                sys.ReleaseOperation();
            }
        }

        [Test]
        public void SerializeAsync_NoSystem_Works()
        {
            using var isolatedWorld = new World("IsolatedWorld");

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "IsolatedWorld");
            var sys = new SaveSystem(def);

            var result = sys.Serialize(isolatedWorld).GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess);
        }

        [Test]
        public void SerializeDeserialize_ViaSystem_EndToEnd()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            var load = _system.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            Assert.IsFalse(sys.IsBusy);
        }
    }
}
