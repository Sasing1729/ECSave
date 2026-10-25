using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    [TestFixture]
    public sealed class FrameBoundaryDeserializeTests
    {
        private World _world;
        private ECSaveSnapshotGroup _group;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenFrameBoundaryWorld");
            _group = _world.CreateSystemManaged<ECSaveSnapshotGroup>();
            var runtimeSys = _world.CreateSystemManaged<SaveRuntimeSystem>();
            _group.AddSystemToUpdateList(runtimeSys);
            _group.SortSystems();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated)
            {
                DisposeWorldCleanly();
            }
        }

        private void DisposeWorldCleanly()
        {
            if (_world == null || !_world.IsCreated) return;

            try
            {
                var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
                sys?.ForceCleanupForTests();
            }
            catch { }

            try { _world.Dispose(); }
            catch { }

            _world = null;
        }

        private int DriveUntilComplete<T>(UniTask<T> op, int maxUpdates = 1000)
        {
            int updates = 0;
            while (!op.Status.IsCompleted() && updates < maxUpdates)
            {
                _group.Update();
                updates++;
            }
            return updates;
        }

        private byte[] MakeSaveData()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var op = sys.Serialize(_world);
                int updates = DriveUntilComplete(op);

                Assert.That(op.Status.IsCompleted(), Is.True,
                    $"Serialize did not complete within {updates} updates " +
                    $"(op status: {op.Status})");

                var save = op.GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True, $"Save failed: {save.ErrorCode}");
                return save.Data;
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        private void RecreateWorld()
        {
            DisposeWorldCleanly();
            _world = new World("GenFrameBoundaryWorld");
            _group = _world.CreateSystemManaged<ECSaveSnapshotGroup>();
            var runtimeSys = _world.CreateSystemManaged<SaveRuntimeSystem>();
            _group.AddSystemToUpdateList(runtimeSys);
            _group.SortSystems();
        }

        [Test]
        public void FrameBoundary_DeserializeCompletesAcrossMultipleUpdates()
        {
            var data = MakeSaveData();

            RecreateWorld();

            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();

                var op = sys.Deserialize(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration);
                Assert.That(op.Status.IsCompleted(), Is.False,
                    "FrameBoundary deserialize should not complete synchronously");

                int updates = DriveUntilComplete(op);

                Assert.That(op.Status.IsCompleted(), Is.True,
                    $"Deserialize did not complete within {updates} updates " +
                    $"(op status: {op.Status})");
                Assert.That(updates, Is.GreaterThanOrEqualTo(2),
                    "Deserialize requires at least prephase+apply stages");

                var load = op.GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True, $"Load failed: {load.ErrorCode}");
                Assert.That(load.EntityCount, Is.EqualTo(1));

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenHealth));
                using var health = q.ToComponentDataArray<GenHealth>(Allocator.TempJob);
                Assert.That(health.Length, Is.EqualTo(1));
                Assert.That(health[0].Current, Is.EqualTo(100));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_DeserializeExtraUpdatesAreNoOp()
        {
            var data = MakeSaveData();

            RecreateWorld();

            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();

                var op = sys.Deserialize(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration);

                DriveUntilComplete(op);
                Assert.That(op.Status.IsCompleted(), Is.True);

                Assert.DoesNotThrow(() => _group.Update());
                Assert.That(op.GetAwaiter().GetResult().IsSuccess, Is.True);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_PreCancelledToken_ReturnsCancelled()
        {
            var data = MakeSaveData();

            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();

                var cts = new System.Threading.CancellationTokenSource();
                cts.Cancel();

                var op = sys.Deserialize(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration, cts.Token);
                Assert.That(op.Status.IsCompleted(), Is.True);
                var result = op.GetAwaiter().GetResult();
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.Cancelled));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_WorldDestroyedAfterPrephase_AbortFires()
        {
            var data = MakeSaveData();

            RecreateWorld();

            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();

                var op = sys.Deserialize(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration);
                Assert.That(op.Status.IsCompleted(), Is.False);

                _group.Update();
                Assert.That(op.Status.IsCompleted(), Is.False);

                DisposeWorldCleanly();

                Assert.That(op.Status.IsCompleted(), Is.True);
                var result = op.GetAwaiter().GetResult();
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.WorldModificationFailed));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_ConcurrentCall_ReturnsConcurrentOperation()
        {
            var data = MakeSaveData();

            RecreateWorld();

            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();

                var op1 = sys.Deserialize(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration);
                Assert.That(op1.Status.IsCompleted(), Is.False);

                var op2 = sys.Deserialize(_world, data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration);
                Assert.That(op2.Status.IsCompleted(), Is.True);
                Assert.That(op2.GetAwaiter().GetResult().ErrorCode,
                    Is.EqualTo(SaveLoadErrorCode.ConcurrentOperation));

                DriveUntilComplete(op1);
                op1.GetAwaiter().GetResult();
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_NoSaveRuntimeSystem_FallsBackToSync()
        {
            var data = MakeSaveData();

            var bareWorld = new World("BareFrameBoundaryWorld");
            try
            {
                var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
                try
                {
                    var sys = scope.GetSaveSystem();

                    var op = sys.Deserialize(bareWorld, data,
                        RestorationScope.EntireWorld, DeletionPolicy.ByRegistration);
                    Assert.That(op.Status.IsCompleted(), Is.True,
                        "Fallback path should complete synchronously");
                    Assert.That(op.GetAwaiter().GetResult().IsSuccess, Is.True);
                }
                finally { ScriptableObject.DestroyImmediate(scope); }
            }
            finally
            {
                try { bareWorld.Dispose(); } catch { }
            }
        }
    }
}
