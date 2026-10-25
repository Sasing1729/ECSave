using Cysharp.Threading.Tasks;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using NUnit.Framework;
using System.Collections;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.TestTools;

namespace ECSave.Tests.Runtime
{
    public class NonBlockingOperationTests
    {
        private World _world;
        private SaveSystem _system;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            PlayerLoopCompletionNotifier.ResetForTests();
            _world = new World("NonBlockingTestWorld");
            _world.CreateSystemManaged<SaveRuntimeSystem>();

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "NonBlockingTestWorld");
            _system = new SaveSystem(def);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerLoopCompletionNotifier.ResetForTests();
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [UnityTest]
        public IEnumerator SerializeAsyncNonBlocking_CompletesNextFrame()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var task = _system.SerializeAsyncNonBlocking(_world);

            Assert.IsFalse(task.Status.IsCompleted(),
                "task should not complete synchronously");

            yield return null;
            yield return null;

            Assert.IsTrue(task.Status.IsCompleted(), "task should complete after a few frames");

            var result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess, $"Serialize failed: {result.ErrorCode}");
            Assert.IsNotNull(result.Data);
        }

        [UnityTest]
        public IEnumerator DeserializeAsyncNonBlocking_CompletesNextFrame()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var save = _system.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            var task = _system.DeserializeAsyncNonBlocking(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration);

            Assert.IsFalse(task.Status.IsCompleted());

            yield return null;
            yield return null;

            Assert.IsTrue(task.Status.IsCompleted());

            var result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess, $"Deserialize failed: {result.ErrorCode}");
        }

        [UnityTest]
        public IEnumerator NonBlocking_CallerReturnsImmediately()
        {
            var em = _world.EntityManager;
            for (int i = 0; i < 100; i++)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new Health { Current = i, Max = 100 });
            }

            var task = _system.SerializeAsyncNonBlocking(_world);

            Assert.IsFalse(task.Status.IsCompleted(),
                "call should return before execution");

            yield return null;
            yield return null;

            var result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess);
        }

        [UnityTest]
        public IEnumerator NonBlocking_SerialBackToBack_FramesSeparate()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 1, Max = 2 });

            var t1 = _system.SerializeAsyncNonBlocking(_world);
            yield return null;
            yield return null;
            var r1 = t1.GetAwaiter().GetResult();
            Assert.IsTrue(r1.IsSuccess);

            var t2 = _system.SerializeAsyncNonBlocking(_world);
            yield return null;
            yield return null;
            var r2 = t2.GetAwaiter().GetResult();
            Assert.IsTrue(r2.IsSuccess);
        }

        [Test]
        public void NonBlocking_NullWorld_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                _system.SerializeAsyncNonBlocking(null));
        }

        [Test]
        public void NonBlocking_NullData_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                _system.DeserializeAsyncNonBlocking(_world, null,
                    RestorationScope.EntireWorld, DeletionPolicy.None));
        }

        [Test]
        public void NonBlocking_PreCancelled_ReturnsCancelledImmediately()
        {
            using var cts = new System.Threading.CancellationTokenSource();
            cts.Cancel();

            var task = _system.SerializeAsyncNonBlocking(_world, cts.Token);
            Assert.IsTrue(task.Status.IsCompleted());

            var result = task.GetAwaiter().GetResult();
            Assert.AreEqual(SaveLoadErrorCode.Cancelled, result.ErrorCode);
        }

        [Test]
        public void NonBlocking_GuardHeld_ReturnsConcurrentOperation()
        {
            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            Assert.IsTrue(sys.TryAcquireOperation());

            try
            {
                var task = _system.SerializeAsyncNonBlocking(_world);
                Assert.IsTrue(task.Status.IsCompleted());

                var result = task.GetAwaiter().GetResult();
                Assert.AreEqual(SaveLoadErrorCode.ConcurrentOperation, result.ErrorCode);
            }
            finally
            {
                sys.ReleaseOperation();
            }
        }

        [UnityTest]
        public IEnumerator NonBlocking_GuardReleased_AfterCompletion()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 1, Max = 2 });

            var task = _system.SerializeAsyncNonBlocking(_world);
            yield return null;
            yield return null;

            var result = task.GetAwaiter().GetResult();
            Assert.IsTrue(result.IsSuccess);

            var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
            Assert.IsFalse(sys.IsBusy);
        }
    }
}
