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
    [TestFixture]
    public sealed class BufferAsyncTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("BufferAsyncWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static GenTestScope CreateScope()
            => ScriptableObject.CreateInstance<GenTestScope>();

        [UnityTest]
        public IEnumerator RoundTrip_SingleBuffer_RoundTrip()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                var buf = em.AddBuffer<GenInventoryItem>(e);
                buf.Add(new GenInventoryItem { ItemId = 10, Count = 1 });
                buf.Add(new GenInventoryItem { ItemId = 20, Count = 2 });
                buf.Add(new GenInventoryItem { ItemId = 30, Count = 3 });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("BufferAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenInventoryItem));
                using var entities = q.ToEntityArray(Allocator.TempJob);
                Assert.That(entities.Length, Is.EqualTo(1));

                var restored = _world.EntityManager.GetBuffer<GenInventoryItem>(entities[0]);
                Assert.That(restored.Length, Is.EqualTo(3));
                Assert.That(restored[0].ItemId, Is.EqualTo(10));
                Assert.That(restored[1].ItemId, Is.EqualTo(20));
                Assert.That(restored[2].ItemId, Is.EqualTo(30));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_EmptyBuffer()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                em.AddBuffer<GenInventoryItem>(e); // empty

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("BufferAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenInventoryItem));
                using var entities = q.ToEntityArray(Allocator.TempJob);
                Assert.That(entities.Length, Is.EqualTo(1));
                Assert.That(_world.EntityManager.GetBuffer<GenInventoryItem>(entities[0]).Length, Is.EqualTo(0));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_MultipleEntities_VaryingCounts()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                // 3 entities: 0, 1, 5 elements
                var elementCounts = new[] { 0, 1, 5 };
                for (int i = 0; i < 3; i++)
                {
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new GenPlayerId { Value = i + 1 });
                    var buf = em.AddBuffer<GenInventoryItem>(e);
                    for (int k = 0; k < elementCounts[i]; k++)
                        buf.Add(new GenInventoryItem { ItemId = i * 100 + k, Count = k });
                }

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("BufferAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var q = _world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<GenInventoryItem>(),
                    ComponentType.ReadOnly<GenPlayerId>());
                using var entities = q.ToEntityArray(Allocator.TempJob);
                Assert.That(entities.Length, Is.EqualTo(3));

                var lengths = new System.Collections.Generic.SortedSet<int>();
                for (int i = 0; i < entities.Length; i++)
                    lengths.Add(_world.EntityManager.GetBuffer<GenInventoryItem>(entities[i]).Length);

                Assert.That(lengths, Does.Contain(0));
                Assert.That(lengths, Does.Contain(1));
                Assert.That(lengths, Does.Contain(5));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_LargeBuffer_100Entities_100Elements()
        {
            const int N = 100;
            const int M = 100;

            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                for (int i = 0; i < N; i++)
                {
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new GenPlayerId { Value = i });
                    var buf = em.AddBuffer<GenInventoryItem>(e);
                    for (int k = 0; k < M; k++)
                        buf.Add(new GenInventoryItem { ItemId = i * 1000 + k, Count = k });
                }

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("BufferAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenInventoryItem));
                using var entities = q.ToEntityArray(Allocator.TempJob);
                Assert.That(entities.Length, Is.EqualTo(N));

                int totalElements = 0;
                for (int i = 0; i < entities.Length; i++)
                    totalElements += _world.EntityManager.GetBuffer<GenInventoryItem>(entities[i]).Length;

                Assert.That(totalElements, Is.EqualTo(N * M));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_ZeroEntities_NoError()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);
                Assert.That(load.EntityCount, Is.EqualTo(0));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }

        [UnityTest]
        public IEnumerator RoundTrip_OrderPreserved()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var e = em.CreateEntity();
                em.AddComponentData(e, new GenPlayerId { Value = 1 });
                var buf = em.AddBuffer<GenInventoryItem>(e);
                for (int k = 0; k < 50; k++)
                    buf.Add(new GenInventoryItem { ItemId = k * 7, Count = k });

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("BufferAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var q = _world.EntityManager.CreateEntityQuery(typeof(GenInventoryItem));
                using var entities = q.ToEntityArray(Allocator.TempJob);
                Assert.That(entities.Length, Is.EqualTo(1));

                var restored = _world.EntityManager.GetBuffer<GenInventoryItem>(entities[0]);
                Assert.That(restored.Length, Is.EqualTo(50));
                for (int k = 0; k < 50; k++)
                {
                    Assert.That(restored[k].ItemId, Is.EqualTo(k * 7));
                    Assert.That(restored[k].Count, Is.EqualTo(k));
                }
            }
            finally { ScriptableObject.DestroyImmediate(scope); }

            yield return null;
        }
    }
}
