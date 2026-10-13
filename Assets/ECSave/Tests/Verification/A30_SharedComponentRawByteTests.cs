using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Tests
{
    public unsafe class A30_SharedComponentRawByteTests
    {
        private World _world;
        private EntityManager _em;

        public struct Faction : ISharedComponentData, System.IEquatable<Faction>
        {
            public int Value;
            public bool Equals(Faction other) => Value == other.Value;
            public override int GetHashCode() => Value;
        }

        [SetUp]
        public void SetUp()
        {
            _world = new World("A30SharedWorld");
            _em = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void GetSharedComponent_ReturnsValue()
        {
            var e = _em.CreateEntity();
            _em.AddSharedComponent(e, new Faction { Value = 42 });

            var value = _em.GetSharedComponent<Faction>(e);
            Assert.AreEqual(42, value.Value);
        }

        [Test]
        public void GetSharedComponent_AddressOf_GivesRawBytes()
        {
            var e = _em.CreateEntity();
            _em.AddSharedComponent(e, new Faction { Value = 123 });

            var value = _em.GetSharedComponent<Faction>(e);
            byte* ptr = (byte*)UnsafeUtility.AddressOf(ref value);

            int rawValue = *(int*)ptr;
            Assert.AreEqual(123, rawValue, "raw byte value mismatch");
            Assert.AreEqual(value.Value, rawValue);
        }

        [Test]
        public void SameValue_SameChunk()
        {
            for (int i = 0; i < 50; i++)
            {
                var e = _em.CreateEntity();
                _em.AddSharedComponent(e, new Faction { Value = 7 });
            }

            var query = _em.CreateEntityQuery(typeof(Faction));
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);

            Assert.AreEqual(1, chunks.Length,
                "same shared value should map to a single chunk");
        }

        [Test]
        public void DifferentValues_DifferentChunks()
        {
            for (int i = 0; i < 3; i++)
            {
                var e = _em.CreateEntity();
                _em.AddSharedComponent(e, new Faction { Value = i });
            }

            var query = _em.CreateEntityQuery(typeof(Faction));
            using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);

            Assert.AreEqual(3, chunks.Length,
                "different shared values should map to different chunks");
        }

        //[Test]
        //public void ChunkSharedComponentIndex_Exists()
        //{
        //    var e = _em.CreateEntity();
        //    _em.AddSharedComponent(e, new Faction { Value = 99 });

        //    var query = _em.CreateEntityQuery(typeof(Faction));
        //    using var chunks = query.ToArchetypeChunkArray(Allocator.TempJob);

        //    Assert.AreEqual(1, chunks.Length);

        //    var handle = _em.GetSharedComponentTypeHandle<Faction>();
        //    var chunk = chunks[0];
        //    int sharedIndex = chunk.GetSharedComponentIndex(ref handle);

        //    Assert.GreaterOrEqual(sharedIndex, 0, "shared component index should be >= 0");
        //}

        [Test]
        public void GetSharedComponent_MultipleEntities_AllReadable()
        {
            var entities = new Entity[6];
            for (int i = 0; i < 6; i++)
            {
                entities[i] = _em.CreateEntity();
                _em.AddSharedComponent(entities[i], new Faction { Value = i % 3 });
            }

            for (int i = 0; i < 6; i++)
            {
                var value = _em.GetSharedComponent<Faction>(entities[i]);
                Assert.AreEqual(i % 3, value.Value, $"entity {i} value mismatch");
            }
        }
    }
}
