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
    public struct GenNamed : IComponentData
    {
        public FixedString32Bytes Name;
        public int Value;
    }

    public struct GenListed : IComponentData
    {
        public FixedList32Bytes<int> Items;
        public int Value;
    }

    public sealed class GenFixedScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenNamed>();
            b.RegisterComponent<GenListed>();
            b.WorldId("GenFixedWorld");
        }
    }

    public class FixedStringFixedListTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenFixedWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [UnityTest]
        public IEnumerator FixedString_Ascii_RoundTrip()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var named = new GenNamed { Value = 7 };
                named.Name = "hello";
                em.AddComponentData(e, named);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenNamed));
                var vals = q.ToComponentDataArray<GenNamed>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, vals.Length);
                    Assert.AreEqual("hello", vals[0].Name.ToString());
                    Assert.AreEqual(7, vals[0].Value);
                }
                finally { vals.Dispose(); }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator FixedString_Japanese_RoundTrip()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var named = new GenNamed { Value = 11 };
                named.Name = "こんにちは";
                em.AddComponentData(e, named);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenNamed));
                var vals = q.ToComponentDataArray<GenNamed>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, vals.Length);
                    Assert.AreEqual("こんにちは", vals[0].Name.ToString());
                }
                finally { vals.Dispose(); }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator FixedString_Empty_RoundTrip()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenNamed { Value = 3 });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenNamed));
                var vals = q.ToComponentDataArray<GenNamed>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(0, vals[0].Name.Length);
                }
                finally { vals.Dispose(); }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator FixedList_RoundTrip()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                var listed = new GenListed { Value = 99 };
                listed.Items.Add(10);
                listed.Items.Add(20);
                listed.Items.Add(30);
                em.AddComponentData(e, listed);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenListed));
                var vals = q.ToComponentDataArray<GenListed>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(1, vals.Length);
                    Assert.AreEqual(3, vals[0].Items.Length);
                    Assert.AreEqual(10, vals[0].Items[0]);
                    Assert.AreEqual(20, vals[0].Items[1]);
                    Assert.AreEqual(30, vals[0].Items[2]);
                    Assert.AreEqual(99, vals[0].Value);
                }
                finally { vals.Dispose(); }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator FixedList_Empty_RoundTrip()
        {
            var scope = ScriptableObject.CreateInstance<GenFixedScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenListed { Value = 1 });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.IsTrue(save.IsSuccess);

                _world.Dispose();
                _world = new World("GenFixedWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenListed));
                var vals = q.ToComponentDataArray<GenListed>(Allocator.TempJob);
                try
                {
                    Assert.AreEqual(0, vals[0].Items.Length);
                    Assert.AreEqual(1, vals[0].Value);
                }
                finally { vals.Dispose(); }
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }
    }
}
