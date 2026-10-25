using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public sealed class GenRollbackScope : SaveScope
    {
        public static Func<IReadOnlyList<SceneLoadInfo>> NextProvider = () => Array.Empty<SceneLoadInfo>();
        public static Func<SceneLoadRequest[], SceneLoadOutcome[]> NextCallback;

        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("GenRollbackWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.RegisterSceneLoadState()
             .WithSceneStateProvider(() => NextProvider())
             .WithSceneRestoreCallback(reqs => NextCallback != null
                 ? NextCallback(reqs)
                 : OkAll(reqs))
             .WithSceneStateRollback();
        }

        private static SceneLoadOutcome[] OkAll(SceneLoadRequest[] reqs)
        {
            var outcomes = new SceneLoadOutcome[reqs.Length];
            for (int i = 0; i < outcomes.Length; i++)
                outcomes[i] = SceneLoadOutcome.Ok();
            return outcomes;
        }
    }

    public sealed class GenNoRollbackScope : SaveScope
    {
        public static Func<IReadOnlyList<SceneLoadInfo>> NextProvider = () => Array.Empty<SceneLoadInfo>();
        public static Func<SceneLoadRequest[], SceneLoadOutcome[]> NextCallback;

        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("GenRollbackWorld");
        }

        protected override void ConfigureSubScene(ISubSceneDefinitionBuilder b)
        {
            b.RegisterSceneLoadState()
             .WithSceneStateProvider(() => NextProvider())
             .WithSceneRestoreCallback(reqs => NextCallback != null
                 ? NextCallback(reqs)
                 : OkAll(reqs));
            // WithSceneStateRollback() を呼ばない
        }

        private static SceneLoadOutcome[] OkAll(SceneLoadRequest[] reqs)
        {
            var outcomes = new SceneLoadOutcome[reqs.Length];
            for (int i = 0; i < outcomes.Length; i++)
                outcomes[i] = SceneLoadOutcome.Ok();
            return outcomes;
        }
    }

    [TestFixture]
    public sealed class SubSceneSceneStateRollbackTests
    {
        private World _world;

        private sealed class TestInjector : IFaultInjector
        {
            public string FailPhase;
            public bool ShouldFail(string phase) => phase == FailPhase;
        }

        private readonly TestInjector _injector = new TestInjector();

        private static readonly SceneGuid GuidA = new SceneGuid(0xA, 0xA);
        private static readonly SceneGuid GuidB = new SceneGuid(0xB, 0xB);

        private sealed class VirtualSceneState
        {
            public Dictionary<SceneGuid, SceneLoadState> State = new();
            public int CallbackCallCount;

            public IReadOnlyList<SceneLoadInfo> Provider()
            {
                var list = new List<SceneLoadInfo>();
                foreach (var kv in State)
                    list.Add(new SceneLoadInfo(kv.Key, kv.Value));
                return list;
            }

            public SceneLoadOutcome[] Callback(SceneLoadRequest[] requests)
            {
                CallbackCallCount++;
                var outcomes = new SceneLoadOutcome[requests.Length];
                for (int i = 0; i < requests.Length; i++)
                {
                    State[requests[i].Guid] = requests[i].TargetState;
                    outcomes[i] = SceneLoadOutcome.Ok();
                }
                return outcomes;
            }
        }

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenRollbackWorld");
            FaultInjector.Current = _injector;
            _injector.FailPhase = null;
            GenRollbackScope.NextProvider = () => Array.Empty<SceneLoadInfo>();
            GenRollbackScope.NextCallback = null;
            GenNoRollbackScope.NextProvider = () => Array.Empty<SceneLoadInfo>();
            GenNoRollbackScope.NextCallback = null;
        }

        [TearDown]
        public void TearDown()
        {
            FaultInjector.Current = null;
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [UnityTest]
        public IEnumerator P7Failure_SceneStateRolledBack()
        {
            var state = new VirtualSceneState();
            state.State[GuidA] = SceneLoadState.Loaded;
            state.State[GuidB] = SceneLoadState.Unloaded;

            GenRollbackScope.NextProvider = () => state.Provider();
            GenRollbackScope.NextCallback = reqs => state.Callback(reqs);

            var scope = ScriptableObject.CreateInstance<GenRollbackScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenRollbackWorld");

                // Simulate external change: A unloaded, B loaded
                state.State[GuidA] = SceneLoadState.Unloaded;
                state.State[GuidB] = SceneLoadState.Loaded;

                int callsBefore = state.CallbackCallCount;

                _injector.FailPhase = "P7";

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.False);
                Assert.That(load.ErrorCode, Is.EqualTo(SaveLoadErrorCode.WorldModificationFailed));

                // P3.5 succeeded: state became [A: Loaded, B: Unloaded]
                // Rollback restored to snapshot [A: Unloaded, B: Loaded]
                Assert.That(state.State[GuidA], Is.EqualTo(SceneLoadState.Unloaded),
                    "Scene state should be rolled back to pre-P3.5 snapshot");
                Assert.That(state.State[GuidB], Is.EqualTo(SceneLoadState.Loaded),
                    "Scene state should be rolled back to pre-P3.5 snapshot");

                Assert.That(state.CallbackCallCount, Is.GreaterThan(callsBefore),
                    "SceneRestoreCallback should have been invoked during rollback");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator P7Failure_NoRollbackOptIn_SceneNotRestored()
        {
            var state = new VirtualSceneState();
            state.State[GuidA] = SceneLoadState.Loaded;
            state.State[GuidB] = SceneLoadState.Unloaded;

            GenNoRollbackScope.NextProvider = () => state.Provider();
            GenNoRollbackScope.NextCallback = reqs => state.Callback(reqs);

            var scope = ScriptableObject.CreateInstance<GenNoRollbackScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenRollbackWorld");

                state.State[GuidA] = SceneLoadState.Unloaded;
                state.State[GuidB] = SceneLoadState.Loaded;

                _injector.FailPhase = "P7";

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.False);

                // Without WithSceneStateRollback, state remains at post-P3.5 values
                Assert.That(state.State[GuidA], Is.EqualTo(SceneLoadState.Loaded),
                    "Without rollback opt-in, P3.5 changes remain");
                Assert.That(state.State[GuidB], Is.EqualTo(SceneLoadState.Unloaded),
                    "Without rollback opt-in, P3.5 changes remain");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator P3_5PartialFailure_SceneStateRolledBack()
        {
            // 修正: LogAssert.Expect は不要（実装側で Warning 出力に変更済み）

            var state = new VirtualSceneState();
            state.State[GuidA] = SceneLoadState.Loaded;
            state.State[GuidB] = SceneLoadState.Unloaded;

            GenRollbackScope.NextProvider = () => state.Provider();
            GenRollbackScope.NextCallback = reqs =>
            {
                var outcomes = new SceneLoadOutcome[reqs.Length];
                for (int i = 0; i < reqs.Length; i++)
                {
                    var r = reqs[i];
                    if (r.Guid == GuidB && r.TargetState == SceneLoadState.Unloaded)
                    {
                        outcomes[i] = SceneLoadOutcome.Fail("injected required failure");
                    }
                    else
                    {
                        state.State[r.Guid] = r.TargetState;
                        outcomes[i] = SceneLoadOutcome.Ok();
                    }
                }
                return outcomes;
            };

            var scope = ScriptableObject.CreateInstance<GenRollbackScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenRollbackWorld");

                state.State[GuidA] = SceneLoadState.Unloaded;
                state.State[GuidB] = SceneLoadState.Loaded;

                _injector.FailPhase = null;

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.False);
                Assert.That(load.ErrorCode, Is.EqualTo(SaveLoadErrorCode.SceneLoadFailed));

                Assert.That(state.State[GuidA], Is.EqualTo(SceneLoadState.Unloaded),
                    "Partially-loaded scene A should be rolled back to Unloaded");
                Assert.That(state.State[GuidB], Is.EqualTo(SceneLoadState.Loaded),
                    "Failed-to-unload scene B should remain Loaded");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RollbackCallbackFails_ReturnsWorldModificationFailed()
        {
            var state = new VirtualSceneState();
            state.State[GuidA] = SceneLoadState.Loaded;
            state.State[GuidB] = SceneLoadState.Unloaded;

            GenRollbackScope.NextProvider = () => state.Provider();

            bool inRollback = false;
            GenRollbackScope.NextCallback = reqs =>
            {
                if (inRollback)
                {
                    // Fail all rollback requests
                    var fail = new SceneLoadOutcome[reqs.Length];
                    for (int i = 0; i < fail.Length; i++)
                        fail[i] = SceneLoadOutcome.Fail("rollback injected failure");
                    return fail;
                }

                // First call (P3.5) succeeds normally
                inRollback = true;
                var outcomes = new SceneLoadOutcome[reqs.Length];
                for (int i = 0; i < reqs.Length; i++)
                {
                    state.State[reqs[i].Guid] = reqs[i].TargetState;
                    outcomes[i] = SceneLoadOutcome.Ok();
                }
                return outcomes;
            };

            var scope = ScriptableObject.CreateInstance<GenRollbackScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 100, Max = 200 });

                var save = sys.Serialize(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenRollbackWorld");

                state.State[GuidA] = SceneLoadState.Unloaded;
                state.State[GuidB] = SceneLoadState.Loaded;

                LogAssert.ignoreFailingMessages = true;
                _injector.FailPhase = "P7";

                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.False);
                Assert.That(load.ErrorCode, Is.EqualTo(SaveLoadErrorCode.WorldModificationFailed),
                    "Rollback callback failure should result in WorldModificationFailed");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }
    }
}
