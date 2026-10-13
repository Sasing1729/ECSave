using System;
using System.Collections.Generic;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.Contract;
using Game.Domain;
using Game.MinimalE2E;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class SaveScopeTests
    {
        private World _world;
        private IReadOnlyDictionary<string, ISaveModuleBinder> _registrySnapshot;

        private sealed class TestScope : SaveScope
        {
            protected override void Configure(ISaveDefinitionBuilder builder)
            {
                var impl = (SaveDefinitionBuilderImpl)builder;
                impl.RegisterModule(new HealthModule());
                impl.RegisterIdentityModule(new PlayerIdModule());
                builder.GameDataVersion(2);
                builder.WorldId("ScopeTestWorld");
            }
        }

        private sealed class TestBinderA : ISaveModuleBinder
        {
            public void BindModules(ISaveDefinitionBuilder builder)
            {
                var impl = (SaveDefinitionBuilderImpl)builder;
                impl.RegisterModule(new StunnedModule());
            }
        }

        private sealed class TestBinderB : ISaveModuleBinder
        {
            public void BindModules(ISaveDefinitionBuilder builder)
            {
                var impl = (SaveDefinitionBuilderImpl)builder;
                impl.RegisterModule(new HealthModule());
            }
        }

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _registrySnapshot = GeneratedScopeRegistry.Snapshot();
            GeneratedScopeRegistry.Remove(typeof(TestScope).FullName);
            _world = new World("ScopeTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            GeneratedScopeRegistry.Restore(_registrySnapshot);
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void GetSaveSystem_BuildsDefinitionFromConfigure()
        {
            var scope = ScriptableObject.CreateInstance<TestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                Assert.IsNotNull(sys);
                Assert.AreEqual(2, sys.Definition.GameDataVersion);
                Assert.AreEqual("ScopeTestWorld", sys.Definition.WorldId);
                Assert.AreEqual(1, sys.Definition.Modules.Count);
                Assert.AreEqual(1, sys.Definition.Identities.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scope);
            }
        }

        [Test]
        public void GetSaveSystem_WithGeneratedBinder_AddsModules()
        {
            var scope = ScriptableObject.CreateInstance<TestScope>();
            GeneratedScopeRegistry.Register(
                typeof(TestScope).FullName, new TestBinderA());

            try
            {
                var sys = scope.GetSaveSystem();
                Assert.AreEqual(2, sys.Definition.Modules.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scope);
            }
        }

        [Test]
        public void GetSaveSystem_SecondCall_ReturnsSameInstance()
        {
            var scope = ScriptableObject.CreateInstance<TestScope>();
            try
            {
                var sys1 = scope.GetSaveSystem();
                var sys2 = scope.GetSaveSystem();
                Assert.AreSame(sys1, sys2);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scope);
            }
        }

        [Test]
        public void Dispose_ThenGetSaveSystem_Throws()
        {
            var scope = ScriptableObject.CreateInstance<TestScope>();
            try
            {
                scope.Dispose();
                Assert.Throws<ObjectDisposedException>(() => scope.GetSaveSystem());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scope);
            }
        }

        [Test]
        public void EndToEnd_SerializeDeserialize_ViaSaveScope()
        {
            var scope = ScriptableObject.CreateInstance<TestScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new PlayerId { Value = 1 });
                em.AddComponentData(e, new Health { Current = 100, Max = 200 });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("ScopeTestWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
                using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
                Assert.AreEqual(1, vals.Length);
                Assert.AreEqual(100, vals[0].Current);
                Assert.AreEqual(200, vals[0].Max);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scope);
            }
        }

        [Test]
        public void GeneratedScopeRegistry_DuplicateDifferentBinder_Throws()
        {
            var id = typeof(TestScope).FullName;

            GeneratedScopeRegistry.Register(id, new TestBinderA());

            Assert.Throws<SaveRegistrationException>(() =>
                GeneratedScopeRegistry.Register(id, new TestBinderB()));
        }

        [Test]
        public void GeneratedScopeRegistry_DuplicateSameType_Ignored()
        {
            var id = typeof(TestScope).FullName;

            GeneratedScopeRegistry.Register(id, new TestBinderA());
            Assert.DoesNotThrow(() =>
                GeneratedScopeRegistry.Register(id, new TestBinderA()));
        }
    }
}
