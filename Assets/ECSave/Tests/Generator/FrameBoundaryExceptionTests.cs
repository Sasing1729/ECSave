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
    public sealed class FrameBoundaryExceptionTests
    {
        private World _world;
        private ECSaveSnapshotGroup _group;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenFrameBoundaryWorld");
            _group = _world.CreateSystemManaged<ECSaveSnapshotGroup>();
            var sys = _world.CreateSystemManaged<SaveRuntimeSystem>();
            _group.AddSystemToUpdateList(sys);
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

        // ★ Unity の World.Dispose は PreDisposeCheck → OnDestroy の順。
        //   PreDisposeCheck が pending job を検出するとエラーログを出す。
        //   SaveRuntimeSystem.ForceCleanupForTests() で OnDestroy 相当の処理を
        //   先に実行し、pending job を完了させてから World.Dispose する。
        private void DisposeWorldCleanly()
        {
            if (_world == null || !_world.IsCreated) return;

            try
            {
                var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
                sys?.ForceCleanupForTests();
            }
            catch
            {
                // ベストエフォート
            }

            try { _world.Dispose(); }
            catch { }

            _world = null;
        }

        private int DriveUntilComplete(UniTask<SaveResult> op, int maxUpdates = 1000)
        {
            int updates = 0;
            while (!op.Status.IsCompleted() && updates < maxUpdates)
            {
                _group.Update();
                updates++;
            }
            return updates;
        }

        [Test]
        public void FrameBoundary_NoSaveRuntimeSystem_FallsBackToSync()
        {
            var bareWorld = new World("BareFrameBoundaryWorld");
            try
            {
                var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
                try
                {
                    var sys = scope.GetSaveSystem();
                    var em = bareWorld.EntityManager;
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new GenHealth { Current = 1, Max = 1 });

                    var op = sys.SerializeAsync(bareWorld);
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

        [Test]
        public void FrameBoundary_ConcurrentCall_ReturnsConcurrentOperation()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 1, Max = 1 });

                var op1 = sys.SerializeAsync(_world);
                Assert.That(op1.Status.IsCompleted(), Is.False);

                var op2 = sys.SerializeAsync(_world);
                Assert.That(op2.Status.IsCompleted(), Is.True);
                var r2 = op2.GetAwaiter().GetResult();
                Assert.That(r2.IsSuccess, Is.False);
                Assert.That(r2.ErrorCode, Is.EqualTo(SaveLoadErrorCode.ConcurrentOperation));

                DriveUntilComplete(op1);
                op1.GetAwaiter().GetResult();
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_WorldDestroyedWhileJobsRunning_AbortFires()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 1, Max = 1 });

                var op = sys.SerializeAsync(_world);
                Assert.That(op.Status.IsCompleted(), Is.False);

                _group.Update();
                Assert.That(op.Status.IsCompleted(), Is.False,
                    "After 1st Update: async capture scheduled, jobs running");

                // ★ ForceCleanupForTests で OnDestroy 相当の処理を先に実行。
                //   pending job が完了し、op.OnAbort が発火して abort 状態になる。
                DisposeWorldCleanly();

                Assert.That(op.Status.IsCompleted(), Is.True);
                var result = op.GetAwaiter().GetResult();
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.SerializationFailed));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_WorldDestroyedAfterMultipleUpdates_AbortFires()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 1, Max = 1 });

                var op = sys.SerializeAsync(_world);

                for (int i = 0; i < 2 && !op.Status.IsCompleted(); i++)
                    _group.Update();

                if (op.Status.IsCompleted())
                {
                    var okResult = op.GetAwaiter().GetResult();
                    Assert.That(okResult.IsSuccess, Is.True);
                    return;
                }

                DisposeWorldCleanly();

                Assert.That(op.Status.IsCompleted(), Is.True);
                var result = op.GetAwaiter().GetResult();
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.SerializationFailed));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_PreCancelledToken_ReturnsCancelled()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 1, Max = 1 });

                var cts = new System.Threading.CancellationTokenSource();
                cts.Cancel();

                var op = sys.SerializeAsync(_world, cts.Token);
                Assert.That(op.Status.IsCompleted(), Is.True);
                var result = op.GetAwaiter().GetResult();
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.Cancelled));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_SecondCallAfterCompletion_Succeeds()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 1, Max = 1 });

                var op1 = sys.SerializeAsync(_world);
                DriveUntilComplete(op1);
                Assert.That(op1.GetAwaiter().GetResult().IsSuccess, Is.True);

                var op2 = sys.SerializeAsync(_world);
                Assert.That(op2.Status.IsCompleted(), Is.False,
                    "Guard should be released after op1");

                DriveUntilComplete(op2);
                Assert.That(op2.GetAwaiter().GetResult().IsSuccess, Is.True);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void FrameBoundary_CancellationBetweenFrames_ReturnsCancelled()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 1, Max = 1 });

                var cts = new System.Threading.CancellationTokenSource();
                var op = sys.SerializeAsync(_world, cts.Token);

                _group.Update();

                cts.Cancel();

                DriveUntilComplete(op);

                Assert.That(op.Status.IsCompleted(), Is.True);
                var result = op.GetAwaiter().GetResult();
                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(SaveLoadErrorCode.Cancelled));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }
    }
}
