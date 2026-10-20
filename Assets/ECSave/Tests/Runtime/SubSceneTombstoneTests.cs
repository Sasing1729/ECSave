using System;
using System.Linq;
using NUnit.Framework;
using ECSave.Internal.SubScene;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime.SubScene
{
    public struct GenTombstoneId : IComponentData
    {
        public int Value;
    }

    public struct GenTombstoneOtherId : IComponentData
    {
        public long Value;
    }

    [TestFixture]
    public sealed class SubSceneTombstoneTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            SubSceneTombstone.ClearForTests();
            _world = new World("TombstoneTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            SubSceneTombstone.ClearForTests();
        }

        [Test]
        public void MarkDestroyed_RoundTrip_SnapshotMatches()
        {
            var em = _world.EntityManager;
            SubSceneTombstone.MarkDestroyed(em, new GenTombstoneId { Value = 42 });
            SubSceneTombstone.MarkDestroyed(em, new GenTombstoneId { Value = 7 });
            SubSceneTombstone.MarkDestroyed(em, new GenTombstoneOtherId { Value = 100L });

            var handlers = SubSceneTombstone.GetRegisteredHandlers();
            Assert.That(handlers.Count, Is.EqualTo(2));

            var idHandler = handlers.First(h => h.IdentityType == typeof(GenTombstoneId));
            var snapshot = idHandler.Snapshot(_world);

            Assert.That(snapshot.Count, Is.EqualTo(2));
            Assert.That(snapshot[0].Length, Is.EqualTo(idHandler.KeyStride));
            Assert.That(snapshot[1].Length, Is.EqualTo(idHandler.KeyStride));

            var em2 = new World("RestoredWorld");
            try
            {
                var singleton = SubSceneTombstoneRegistry.GetOrCreate(em2.EntityManager);
                idHandler.RestoreToRegistry(em2.EntityManager, singleton, snapshot);
                var snapshot2 = idHandler.Snapshot(em2);

                Assert.That(snapshot2.Count, Is.EqualTo(2));
                for (int i = 0; i < snapshot.Count; i++)
                    CollectionAssert.AreEqual(snapshot[i], snapshot2[i]);
            }
            finally { em2.Dispose(); }
        }

        [Test]
        public void Snapshot_NoRegistry_ReturnsEmpty()
        {
            SubSceneTombstone.Register<GenTombstoneId>();
            var handlers = SubSceneTombstone.GetRegisteredHandlers();
            var h = handlers.First(x => x.IdentityType == typeof(GenTombstoneId));

            var snapshot = h.Snapshot(_world);
            Assert.That(snapshot.Count, Is.EqualTo(0));
        }

        [Test]
        public void RestoreToRegistry_Empty_KeepsExistingBuffer()
        {
            var em = _world.EntityManager;
            SubSceneTombstone.MarkDestroyed(em, new GenTombstoneId { Value = 1 });

            var handlers = SubSceneTombstone.GetRegisteredHandlers();
            var h = handlers.First(x => x.IdentityType == typeof(GenTombstoneId));
            var singleton = SubSceneTombstoneRegistry.GetOrCreate(em);

            h.RestoreToRegistry(em, singleton, Array.Empty<byte[]>());

            // 空の keys は早期リターンし、既存 Buffer は変更されない（意図的仕様）
            var snapshot = h.Snapshot(_world);
            Assert.That(snapshot.Count, Is.EqualTo(1));
        }

        [Test]
        public void CollectMatches_MatchingKey_AddsEntity()
        {
            var em = _world.EntityManager;

            var e1 = em.CreateEntity(typeof(GenTombstoneId));
            em.SetComponentData(e1, new GenTombstoneId { Value = 42 });
            var e2 = em.CreateEntity(typeof(GenTombstoneId));
            em.SetComponentData(e2, new GenTombstoneId { Value = 7 });
            var e3 = em.CreateEntity(typeof(GenTombstoneId));
            em.SetComponentData(e3, new GenTombstoneId { Value = 100 });

            SubSceneTombstone.MarkDestroyed(em, new GenTombstoneId { Value = 42 });
            SubSceneTombstone.MarkDestroyed(em, new GenTombstoneId { Value = 100 });

            var handlers = SubSceneTombstone.GetRegisteredHandlers();
            var h = handlers.First(x => x.IdentityType == typeof(GenTombstoneId));
            var singleton = SubSceneTombstoneRegistry.GetOrCreate(em);

            var output = new NativeList<Entity>(Allocator.TempJob);
            try
            {
                h.CollectMatches(em, singleton, output);

                Assert.That(output.Length, Is.EqualTo(2));
                bool has1 = false, has3 = false;
                for (int i = 0; i < output.Length; i++)
                {
                    if (output[i] == e1) has1 = true;
                    if (output[i] == e3) has3 = true;
                }
                Assert.That(has1, Is.True);
                Assert.That(has3, Is.True);
            }
            finally { output.Dispose(); }
        }

        [Test]
        public void CollectMatches_EmptyBuffer_NoMatches()
        {
            var em = _world.EntityManager;
            var e = em.CreateEntity(typeof(GenTombstoneId));
            em.SetComponentData(e, new GenTombstoneId { Value = 42 });

            SubSceneTombstone.Register<GenTombstoneId>();
            var handlers = SubSceneTombstone.GetRegisteredHandlers();
            var h = handlers.First(x => x.IdentityType == typeof(GenTombstoneId));
            var singleton = SubSceneTombstoneRegistry.GetOrCreate(em);

            var output = new NativeList<Entity>(Allocator.TempJob);
            try
            {
                h.CollectMatches(em, singleton, output);
                Assert.That(output.Length, Is.EqualTo(0));
            }
            finally { output.Dispose(); }
        }
    }
}
