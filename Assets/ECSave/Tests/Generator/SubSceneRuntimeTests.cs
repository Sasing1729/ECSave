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
    [TestFixture]
    public sealed class SubSceneRuntimeTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenSubSceneWorld");
            GenSubSceneScope.NextProviderResult = System.Array.Empty<SceneLoadInfo>();
            GenSubSceneScope.NextCallback = null;
            GenSubSceneWithoutCallbackScope.NextProviderResult = System.Array.Empty<SceneLoadInfo>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static T CreateScope<T>() where T : SaveScope
            => ScriptableObject.CreateInstance<T>();

        [Test]
        public void Definition_SubScene_ReflectsFlags()
        {
            var scope = CreateScope<GenSubSceneScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var sub = sys.Definition.SubScene;
                Assert.That(sub, Is.Not.Null);
                Assert.That(sub.RegisterSceneLoadState, Is.True);
                Assert.That(sub.SceneStateProvider, Is.Not.Null);
                Assert.That(sub.SceneRestoreCallback, Is.Not.Null);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void Definition_PlainScope_SubSceneIsEmpty()
        {
            var scope = CreateScope<GenTestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                Assert.That(sys.Definition.SubScene.HasAnyConfiguration, Is.False);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void PrefabMigrationScope_DefinitionContainsMigration()
        {
            var scope = CreateScope<GenPrefabMigrationScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                Assert.That(sys.Definition.SubScene.PrefabMigrations.Count, Is.EqualTo(1));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [UnityTest]
        public IEnumerator SubScene_RoundTrip_ProviderAndCallbackInvoked()
        {
            var scope = CreateScope<GenSubSceneScope>();
            try
            {
                var guid = new SceneGuid(0xAAA, 0xBBB);
                GenSubSceneScope.NextProviderResult = new[]
                {
                    new SceneLoadInfo(guid, SceneLoadState.Loaded, SceneRestoreMode.Required),
                };

                SceneLoadRequest[] captured = null;
                GenSubSceneScope.NextCallback = reqs =>
                {
                    captured = reqs;
                    var outcomes = new SceneLoadOutcome[reqs.Length];
                    for (int i = 0; i < outcomes.Length; i++)
                        outcomes[i] = SceneLoadOutcome.Ok();
                    return outcomes;
                };

                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 10, Max = 100 });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("GenSubSceneWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.True, $"Deserialize: {load.ErrorCode}");
                Assert.That(captured, Is.Not.Null, "SceneRestoreCallback should have been invoked");
                Assert.That(captured.Length, Is.EqualTo(1));
                Assert.That(captured[0].Guid, Is.EqualTo(guid));
                Assert.That(captured[0].TargetState, Is.EqualTo(SceneLoadState.Loaded));
                Assert.That(captured[0].Mode, Is.EqualTo(SceneRestoreMode.Required));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SubScene_RequiredFailure_ReturnsSceneLoadFailed()
        {
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(".*Required scene restore failed.*"));

            var scope = CreateScope<GenSubSceneScope>();
            try
            {
                GenSubSceneScope.NextProviderResult = new[]
                {
            new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required),
        };
                GenSubSceneScope.NextCallback = reqs =>
                {
                    var outcomes = new SceneLoadOutcome[reqs.Length];
                    for (int i = 0; i < outcomes.Length; i++)
                        outcomes[i] = SceneLoadOutcome.Fail("required failed");
                    return outcomes;
                };

                var sys = scope.GetSaveSystem();
                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenSubSceneWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.False);
                Assert.That(load.ErrorCode, Is.EqualTo(SaveLoadErrorCode.SceneLoadFailed));
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SubScene_OptionalFailure_EmitsWarning()
        {
            var scope = CreateScope<GenSubSceneScope>();
            try
            {
                GenSubSceneScope.NextProviderResult = new[]
                {
            new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Optional),
        };
                GenSubSceneScope.NextCallback = reqs =>
                {
                    var outcomes = new SceneLoadOutcome[reqs.Length];
                    for (int i = 0; i < outcomes.Length; i++)
                        outcomes[i] = SceneLoadOutcome.Fail("optional failed");
                    return outcomes;
                };

                var sys = scope.GetSaveSystem();
                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenSubSceneWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.That(load.IsSuccess, Is.True);
                bool hasWarn = false;
                for (int i = 0; i < load.Warnings.Count; i++)
                    if (load.Warnings[i].Kind == LoadWarningKind.SceneRestoreFailed)
                        hasWarn = true;
                Assert.That(hasWarn, Is.True, "SceneRestoreFailed warning should be recorded");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SubScene_NoCallback_RestoreSkipped()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*Scene restore requested but no SceneRestoreCallback.*"));

            GenSubSceneWithoutCallbackScope.NextProviderResult = new[]
            {
                new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded, SceneRestoreMode.Required),
            };

            var scope = CreateScope<GenSubSceneWithoutCallbackScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("GenSubSceneNoCbWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                // 警告のみで成功（復元はスキップ）
                Assert.That(load.IsSuccess, Is.True);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
                GenSubSceneWithoutCallbackScope.NextProviderResult = System.Array.Empty<SceneLoadInfo>();
            }

            yield return null;
        }

        [Test]
        public void Definition_WithContentVerification_PassesThrough()
        {
            var builder = new SubSceneDefinitionBuilderImpl();
            builder.WithContentVerification(ContentVerificationMode.GuidAndContentHash);
            var def = builder.Build();
            Assert.That(def.ContentVerification, Is.EqualTo(ContentVerificationMode.GuidAndContentHash));
        }

        [Test]
        public void Definition_WithStreamingConsistency_PassesThrough()
        {
            var builder = new SubSceneDefinitionBuilderImpl();
            builder.WithStreamingConsistency(StreamingConsistencyMode.FrameBoundary);
            var def = builder.Build();
            Assert.That(def.StreamingConsistency, Is.EqualTo(StreamingConsistencyMode.FrameBoundary));
        }
    }
}
