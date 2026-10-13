using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ECSave;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Generator
{
    public sealed class GenVersion1Scope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.GameDataVersion(1);
            b.WorldId("GenVersionWorld");
        }
    }

    public sealed class GenVersion5Scope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.GameDataVersion(5);
            b.WorldId("GenVersionWorld");
        }
    }

    public sealed class GenWorldIdAScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("WorldA");
        }
    }

    public sealed class GenWorldIdBScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenHealth>();
            b.WorldId("WorldB");
        }
    }

    public class VersionMismatchTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("WorldA");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static T Create<T>() where T : SaveScope
            => ScriptableObject.CreateInstance<T>();

        [UnityTest]
        public IEnumerator NewerGameDataVersion_Rejected()
        {
            var saveScope = Create<GenVersion5Scope>();
            var loadScope = Create<GenVersion1Scope>();
            try
            {
                var saveSys = saveScope.GetSaveSystem();
                var loadSys = loadScope.GetSaveSystem();

                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 42, Max = 100 });

                var save = saveSys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = loadSys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.IsFalse(load.IsSuccess);
                Assert.AreEqual(SaveLoadErrorCode.VersionMismatch, load.ErrorCode);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(saveScope);
                ScriptableObject.DestroyImmediate(loadScope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator OlderGameDataVersion_Accepted()
        {
            var saveScope = Create<GenVersion1Scope>();
            var loadScope = Create<GenVersion5Scope>();
            try
            {
                var saveSys = saveScope.GetSaveSystem();
                var loadSys = loadScope.GetSaveSystem();

                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 42, Max = 100 });

                var save = saveSys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = loadSys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.IsTrue(load.IsSuccess, $"Expected success: {load.ErrorCode}");
            }
            finally
            {
                ScriptableObject.DestroyImmediate(saveScope);
                ScriptableObject.DestroyImmediate(loadScope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator WorldIdMismatch_Rejected()
        {
            var saveScope = Create<GenWorldIdAScope>();
            var loadScope = Create<GenWorldIdBScope>();
            try
            {
                var saveSys = saveScope.GetSaveSystem();
                var loadSys = loadScope.GetSaveSystem();

                var em = _world.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new GenHealth { Current = 42, Max = 100 });

                var save = saveSys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                var load = loadSys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();

                Assert.IsFalse(load.IsSuccess);
                Assert.AreEqual(SaveLoadErrorCode.WorldMismatch, load.ErrorCode);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(saveScope);
                ScriptableObject.DestroyImmediate(loadScope);
            }

            yield return null;
        }
    }
}
