using System.Threading;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class OperationExtendedTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("OperationExtTestWorld");
            _world.CreateSystemManaged<SaveRuntimeSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private SaveSystem CreateSystem(string worldId = "OperationExtTestWorld")
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: worldId);
            return new SaveSystem(def);
        }

        private SaveRuntimeSystem GetRuntime()
            => _world.GetExistingSystemManaged<SaveRuntimeSystem>();

        [Test]
        public void SerializeAsync_NullWorld_Throws()
        {
            var sys = CreateSystem();
            Assert.Throws<System.ArgumentNullException>(() =>
                sys.SerializeAsync(null).GetAwaiter().GetResult());
        }

        [Test]
        public void SerializeAsync_DisposedWorld_Throws()
        {
            var sys = CreateSystem();
            var w = new World("Disposed");
            w.Dispose();

            Assert.Throws<System.ObjectDisposedException>(() =>
                sys.SerializeAsync(w).GetAwaiter().GetResult());
        }

        [Test]
        public void DeserializeAsync_NullWorld_Throws()
        {
            var sys = CreateSystem();
            Assert.Throws<System.ArgumentNullException>(() =>
                sys.DeserializeAsync(null, new byte[] { 1 },
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void DeserializeAsync_DisposedWorld_Throws()
        {
            var sys = CreateSystem();
            var w = new World("Disposed2");
            w.Dispose();

            Assert.Throws<System.ObjectDisposedException>(() =>
                sys.DeserializeAsync(w, new byte[] { 1 },
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void DeserializeAsync_NullData_Throws()
        {
            var sys = CreateSystem();
            Assert.Throws<System.ArgumentNullException>(() =>
                sys.DeserializeAsync(_world, null,
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void SerializeBlockedByDeserializeGuard()
        {
            var sys = GetRuntime();
            Assert.IsTrue(sys.TryAcquireOperation());

            try
            {
                var saveSys = CreateSystem();
                var result = saveSys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.AreEqual(SaveLoadErrorCode.ConcurrentOperation, result.ErrorCode);
            }
            finally
            {
                sys.ReleaseOperation();
            }
        }

        [Test]
        public void DeserializeBlockedBySerializeGuard()
        {
            var sys = GetRuntime();
            Assert.IsTrue(sys.TryAcquireOperation());

            try
            {
                var saveSys = CreateSystem();
                var result = saveSys.DeserializeAsync(_world, new byte[] { 1, 2, 3, 4 },
                    RestorationScope.EntireWorld, DeletionPolicy.None)
                    .GetAwaiter().GetResult();
                Assert.AreEqual(SaveLoadErrorCode.ConcurrentOperation, result.ErrorCode);
            }
            finally
            {
                sys.ReleaseOperation();
            }
        }

        [Test]
        public void DifferentSaveSystemInstances_SameWorld_AreMutuallyExclusive()
        {
            var sys1 = CreateSystem();
            var sys2 = CreateSystem();

            var runtime = GetRuntime();
            Assert.IsTrue(runtime.TryAcquireOperation());

            try
            {
                var r1 = sys1.SerializeAsync(_world).GetAwaiter().GetResult();
                var r2 = sys2.SerializeAsync(_world).GetAwaiter().GetResult();

                Assert.AreEqual(SaveLoadErrorCode.ConcurrentOperation, r1.ErrorCode);
                Assert.AreEqual(SaveLoadErrorCode.ConcurrentOperation, r2.ErrorCode);
            }
            finally
            {
                runtime.ReleaseOperation();
            }
        }

        [Test]
        public void DifferentWorlds_IndependentGuards()
        {
            using var world2 = new World("IndependentWorld");
            world2.CreateSystemManaged<SaveRuntimeSystem>();

            var sys1 = CreateSystem("OperationExtTestWorld");
            var sys2 = CreateSystem("IndependentWorld");

            var r1 = sys1.SerializeAsync(_world).GetAwaiter().GetResult();
            var r2 = sys2.SerializeAsync(world2).GetAwaiter().GetResult();

            Assert.IsTrue(r1.IsSuccess);
            Assert.IsTrue(r2.IsSuccess);
        }

        [Test]
        public void AfterFailedDeserialize_GuardReleased()
        {
            var sys = CreateSystem();

            var fail = sys.DeserializeAsync(_world, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();
            Assert.IsFalse(fail.IsSuccess);

            var runtime = GetRuntime();
            Assert.IsFalse(runtime.IsBusy);

            var ok = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(ok.IsSuccess);
        }

        [Test]
        public void AfterSerializeSuccess_GuardReleased()
        {
            var sys = CreateSystem();
            var r = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(r.IsSuccess);

            var runtime = GetRuntime();
            Assert.IsFalse(runtime.IsBusy);
        }

        [Test]
        public void MultipleSequentialOperations_AllSucceed()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 1, Max = 2 });

            for (int i = 0; i < 5; i++)
            {
                var r = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(r.IsSuccess, $"iteration {i}: {r.ErrorCode}");
            }

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            for (int i = 0; i < 3; i++)
            {
                var l = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(l.IsSuccess, $"iteration {i}: {l.ErrorCode}");
            }
        }

        [Test]
        public void OperationKind_Serialize_CorrectlySet()
        {
            var op = new Operation(OperationKind.Serialize, default);
            Assert.AreEqual(OperationKind.Serialize, op.Kind);
            Assert.AreEqual(OperationPhase.S0_Guard, op.Phase);
        }

        [Test]
        public void OperationKind_Deserialize_CorrectlySet()
        {
            var op = new Operation(OperationKind.Deserialize, default);
            Assert.AreEqual(OperationKind.Deserialize, op.Kind);
            Assert.AreEqual(OperationPhase.D0_Guard, op.Phase);
        }

        [Test]
        public void Operation_PhaseProgression_Works()
        {
            var op = new Operation(OperationKind.Serialize, default);
            op.SetPhase(OperationPhase.S1_AwaitSnapshot);
            Assert.AreEqual(OperationPhase.S1_AwaitSnapshot, op.Phase);
            op.SetPhase(OperationPhase.S4_Complete);
            Assert.AreEqual(OperationPhase.S4_Complete, op.Phase);
        }

        [Test]
        public void Operation_PendingError_FirstWins()
        {
            var op = new Operation(OperationKind.Deserialize, default);
            op.SetPendingError(new ErrorInfo(SaveLoadErrorCode.ValidationFailed, "first"));
            op.SetPendingError(new ErrorInfo(SaveLoadErrorCode.CorruptedData, "second"));
            Assert.AreEqual(SaveLoadErrorCode.ValidationFailed, op.PendingError.Code);
        }

        [Test]
        public void Operation_MarkComplete_Idempotent()
        {
            var op = new Operation(OperationKind.Serialize, default);
            Assert.IsFalse(op.IsComplete);
            op.MarkComplete();
            Assert.IsTrue(op.IsComplete);
            op.MarkComplete();
            Assert.IsTrue(op.IsComplete);
        }

        [Test]
        public void Operation_MarkWorldDestroyed_SetsBothFlags()
        {
            var op = new Operation(OperationKind.Deserialize, default);
            op.MarkWorldDestroyed();
            Assert.IsTrue(op.WorldDestroyed);
            Assert.IsTrue(op.IsComplete);
        }

        [Test]
        public void SaveRuntimeSystem_OnDestroy_MarksCurrentOperation()
        {
            var sys = GetRuntime();
            var op = new Operation(OperationKind.Serialize, default);
            sys.SetCurrent(op);

            _world.Dispose();

            Assert.IsTrue(op.WorldDestroyed);
            Assert.IsTrue(op.IsComplete);

            _world = null;
        }

        [Test]
        public void SaveRuntimeSystem_OnDestroy_ReleasesGuard()
        {
            var sys = GetRuntime();
            sys.TryAcquireOperation();
            Assert.IsTrue(sys.IsBusy);

            _world.Dispose();

            // 新しい World を作って再テスト
            _world = new World("AfterDestroy");
            _world.CreateSystemManaged<SaveRuntimeSystem>();

            var newSys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            Assert.IsTrue(newSys.TryAcquireOperation());
            newSys.ReleaseOperation();
        }
    }
}
