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
    public sealed class FrameBoundaryModeTests
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
            if (_world == null || !_world.IsCreated) return;

            // ★ 追加: World.Dispose の PreDisposeCheck が pending job を検出する前に
            //   SaveRuntimeSystem の OnDestroy 相当を手動実行し、job をクリーンアップする。
            try
            {
                var sys = _world.GetExistingSystemManaged<SaveRuntimeSystem>();
                sys?.ForceCleanupForTests();
            }
            catch { }

            try { _world.Dispose(); } catch { }
            _world = null;
        }

        // ★ 修正: [Test] → [UnityTest] に変更。
        //   [Test] は 1 フレームも進めないため、ToEntityListAsync が schedule した
        //   async job が worker thread に流れず、JobHandle.IsCompleted が永久に false に
        //   なってしまう。yield return null で 1 フレーム進めて job に実行機会を与える。
        [UnityTest]
        public IEnumerator FrameBoundary_CompletesAcrossMultipleUpdates()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var op = sys.Serialize(_world);
                Assert.That(op.Status.IsCompleted(), Is.False,
                    "FrameBoundary serialize should not complete synchronously");

                int updates = 0;
                while (!op.Status.IsCompleted() && updates < 100)
                {
                    _group.Update();
                    updates++;
                    yield return null;
                }

                Assert.That(op.Status.IsCompleted(), Is.True,
                    $"Should complete within 100 updates (took {updates})");
                Assert.That(updates, Is.GreaterThanOrEqualTo(2),
                    "Non-blocking driving requires at least Schedule + Encode stages");

                var save = op.GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);
                Assert.That(save.Data, Is.Not.Null);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        // TimeBased モードは同期パス（.Execute → .Complete()）を使うので、
        // [Test] のままで問題なし。
        [Test]
        public void TimeBasedMode_CompletesSynchronously()
        {
            var scope = ScriptableObject.CreateInstance<GenTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var op = sys.Serialize(_world);
                Assert.That(op.Status.IsCompleted(), Is.True,
                    "TimeBased mode completes synchronously (Execute blocks on jobs)");

                var save = op.GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        // ★ 修正: [Test] → [UnityTest] に変更。理由は上と同じ。
        [UnityTest]
        public IEnumerator FrameBoundary_ExtraUpdatesAfterCompletion_AreNoOp()
        {
            var scope = ScriptableObject.CreateInstance<GenFrameBoundaryScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 1, Max = 1 });

                var op = sys.Serialize(_world);
                int updates = 0;
                while (!op.Status.IsCompleted() && updates < 100)
                {
                    _group.Update();
                    updates++;
                    yield return null;
                }

                Assert.That(op.Status.IsCompleted(), Is.True,
                    $"Should complete within 100 updates (took {updates})");

                // 完了後の追加 Update は no-op（エラーもなければ op も変化しない）
                for (int i = 0; i < 5; i++)
                    Assert.DoesNotThrow(() => _group.Update());
                yield return null;

                var save = op.GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }
    }
}
