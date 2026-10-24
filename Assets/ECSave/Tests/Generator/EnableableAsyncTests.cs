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
    public sealed class EnableableAsyncTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("EnableableAsyncWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static GenTestScope CreateScope()
            => ScriptableObject.CreateInstance<GenTestScope>();

        private static bool IsEnabled(EntityManager em, Entity e)
            => em.IsComponentEnabled<GenStunned>(e);

        [Test]
        public void RoundTrip_MixedEnabled_AllSavedCorrectly()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var entities = new Entity[5];
                for (int i = 0; i < 5; i++)
                {
                    entities[i] = em.CreateEntity();
                    em.AddComponentData(entities[i], new GenPlayerId { Value = i + 1 });
                    em.AddComponentData(entities[i], new GenStunned { FramesRemaining = i * 10 });
                }
                // Disable 3 (indices 0, 2, 4)
                em.SetComponentEnabled<GenStunned>(entities[0], false);
                em.SetComponentEnabled<GenStunned>(entities[2], false);
                em.SetComponentEnabled<GenStunned>(entities[4], false);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True, $"Serialize: {save.ErrorCode}");

                _world.Dispose();
                _world = new World("EnableableAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True, $"Deserialize: {load.ErrorCode}");

                var em2 = _world.EntityManager;
                var q = em2.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<GenStunned>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                });
                using var restored = q.ToEntityArray(Allocator.TempJob);

                Assert.That(restored.Length, Is.EqualTo(5),
                    "All 5 entities (enabled + disabled) must be restored");

                int enabledCount = 0;
                for (int i = 0; i < restored.Length; i++)
                    if (IsEnabled(em2, restored[i])) enabledCount++;

                Assert.That(enabledCount, Is.EqualTo(2),
                    "Only entities 1 and 3 were enabled; 2 should be enabled after load");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void RoundTrip_AllDisabled_Preserved()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                var entities = new Entity[3];
                for (int i = 0; i < 3; i++)
                {
                    entities[i] = em.CreateEntity();
                    em.AddComponentData(entities[i], new GenPlayerId { Value = i + 1 });
                    em.AddComponentData(entities[i], new GenStunned { FramesRemaining = i });
                    em.SetComponentEnabled<GenStunned>(entities[i], false);
                }

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("EnableableAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var em2 = _world.EntityManager;
                var q = em2.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<GenStunned>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                });
                using var restored = q.ToEntityArray(Allocator.TempJob);
                Assert.That(restored.Length, Is.EqualTo(3));

                for (int i = 0; i < restored.Length; i++)
                    Assert.That(IsEnabled(em2, restored[i]), Is.False,
                        "All entities should be disabled after load");
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void RoundTrip_AllEnabled_Preserved()
        {
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                for (int i = 0; i < 3; i++)
                {
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new GenPlayerId { Value = i + 1 });
                    em.AddComponentData(e, new GenStunned { FramesRemaining = i });
                }

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("EnableableAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var em2 = _world.EntityManager;
                var q = em2.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<GenStunned>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                });
                using var restored = q.ToEntityArray(Allocator.TempJob);
                Assert.That(restored.Length, Is.EqualTo(3));

                for (int i = 0; i < restored.Length; i++)
                    Assert.That(IsEnabled(em2, restored[i]), Is.True);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void RoundTrip_LargeCount_HalfDisabled()
        {
            const int N = 1000;
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                for (int i = 0; i < N; i++)
                {
                    var e = em.CreateEntity();
                    em.AddComponentData(e, new GenPlayerId { Value = i });
                    em.AddComponentData(e, new GenStunned { FramesRemaining = i });
                    if ((i & 1) == 0)
                        em.SetComponentEnabled<GenStunned>(e, false);
                }

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("EnableableAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var em2 = _world.EntityManager;
                var q = em2.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<GenStunned>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                });
                using var restored = q.ToEntityArray(Allocator.TempJob);
                Assert.That(restored.Length, Is.EqualTo(N));

                int enabledCount = 0;
                for (int i = 0; i < restored.Length; i++)
                    if (IsEnabled(em2, restored[i])) enabledCount++;

                Assert.That(enabledCount, Is.EqualTo(N / 2));
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }

        [Test]
        public void RoundTrip_ZeroEntities_NoError()
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
        }

        [Test]
        public void RoundTrip_EntityWithoutStunned_NotAffected()
        {
            // Entity without GenStunned should not appear in the Stunned section
            var scope = CreateScope();
            try
            {
                var sys = scope.GetSaveSystem();
                var em = _world.EntityManager;

                // Entity with only GenHealth (no Stunned)
                var e1 = em.CreateEntity();
                em.AddComponentData(e1, new GenHealth { Current = 50, Max = 100 });

                // Entity with Stunned disabled
                var e2 = em.CreateEntity();
                em.AddComponentData(e2, new GenPlayerId { Value = 1 });
                em.AddComponentData(e2, new GenStunned { FramesRemaining = 1 });
                em.SetComponentEnabled<GenStunned>(e2, false);

                var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
                Assert.That(save.IsSuccess, Is.True);

                _world.Dispose();
                _world = new World("EnableableAsyncWorld");

                var load = sys.DeserializeAsync(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.That(load.IsSuccess, Is.True);

                var em2 = _world.EntityManager;

                var q1 = em2.CreateEntityQuery(typeof(GenHealth));
                Assert.That(q1.CalculateEntityCount(), Is.EqualTo(1));

                var q2 = em2.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<GenStunned>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState,
                });
                using var stunned = q2.ToEntityArray(Allocator.TempJob);
                Assert.That(stunned.Length, Is.EqualTo(1));
                Assert.That(IsEnabled(em2, stunned[0]), Is.False);
            }
            finally { ScriptableObject.DestroyImmediate(scope); }
        }
    }
}
