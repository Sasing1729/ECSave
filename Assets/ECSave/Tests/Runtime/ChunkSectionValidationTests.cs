using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public class ChunkSectionValidationTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("ChunkValidationTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private SaveSystem CreateSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[]
                {
                    new HealthModule(),
                    new ChunkDataModule(),
                },
                new IdentityModule[] { new PlayerIdModule() },
                1, "ChunkValidationTestWorld");
            return new SaveSystem(def);
        }

        private byte[] SerializeWithChunks(int entityCount, int chunkValue)
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var archetype = em.CreateArchetype(
                ComponentType.ReadWrite<PlayerId>(),
                ComponentType.ReadWrite<Health>(),
                ComponentType.ChunkComponent<ChunkData>());

            using var entities = em.CreateEntity(archetype, entityCount, Allocator.Temp);
            for (int i = 0; i < entityCount; i++)
            {
                em.SetComponentData(entities[i], new PlayerId { Value = i + 1 });
                em.SetComponentData(entities[i], new Health { Current = 100, Max = 200 });
            }

            var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<ChunkData>() },
            });
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            if (chunks.Length > 0)
                em.SetChunkComponentData(chunks[0], new ChunkData { Value = chunkValue });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            var err = SaveCompression.TryDecompress(save.Data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);
            return raw;
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("ChunkValidationTestWorld");
        }

        private static int FindChunkSectionOffset(byte[] data)
        {
            unsafe
            {
                fixed (byte* p = data)
                {
                    var view = new ECSave.Format.ByteView(p, data.Length);
                    var r = new ECSave.Format.FormatReader(view);
                    var header = r.ReadHeader();
                    var table = r.ReadSectionTable();

                    for (int i = 0; i < table.Count; i++)
                        if (table.Entries[i].Kind == ECSave.Format.SectionKind.ChunkComponent)
                            return (int)table.Entries[i].Offset;
                    return -1;
                }
            }
        }

        private static int FindTypeDescriptorSize(byte[] data, int sectionOffset)
        {
            unsafe
            {
                fixed (byte* p = data)
                    return (int)(*(uint*)(p + sectionOffset));
            }
        }

        private static void RecomputeChecksum(byte[] data)
        {
            unsafe
            {
                fixed (byte* p = data)
                {
                    int checksumEnd = data.Length - ECSave.Format.ECSaveFormatConstants.FooterSize;
                    ulong ck = ChecksumCalculator.Compute(
                        new ECSave.Format.ByteView(p, checksumEnd));
                    *(ulong*)(p + checksumEnd) = ck;
                }
            }
        }

        [Test]
        public void DuplicateMemberEntityIndex_ReturnsValidationFailed()
        {
            var data = SerializeWithChunks(3, 42);
            int secOff = FindChunkSectionOffset(data);
            Assert.GreaterOrEqual(secOff, 0);

            int tdSize = FindTypeDescriptorSize(data, secOff);
            int bodyOff = secOff + tdSize;

            unsafe
            {
                fixed (byte* p = data)
                {
                    uint* memberOffset = (uint*)(p + bodyOff + 12);
                    Assert.GreaterOrEqual(memberOffset[1], 2u);

                    uint* memberEntityIndex = memberOffset + 2;
                    memberEntityIndex[1] = memberEntityIndex[0];
                }
            }
            RecomputeChecksum(data);

            RestartWorld();
            var sys = CreateSystem();
            var load = sys.DeserializeAsync(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ValidationFailed, load.ErrorCode);
        }

        [Test]
        public void OutOfRangeMemberEntityIndex_ReturnsValidationFailed()
        {
            var data = SerializeWithChunks(3, 42);
            int secOff = FindChunkSectionOffset(data);
            Assert.GreaterOrEqual(secOff, 0);

            int tdSize = FindTypeDescriptorSize(data, secOff);
            int bodyOff = secOff + tdSize;

            unsafe
            {
                fixed (byte* p = data)
                {
                    uint* memberOffset = (uint*)(p + bodyOff + 12);
                    uint* memberEntityIndex = memberOffset + 2;
                    memberEntityIndex[0] = 0xFFFFu;
                }
            }
            RecomputeChecksum(data);

            RestartWorld();
            var sys = CreateSystem();
            var load = sys.DeserializeAsync(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ValidationFailed, load.ErrorCode);
        }

        [Test]
        public void EmptyChunkSection_PassesValidation()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            for (int i = 0; i < 3; i++)
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new PlayerId { Value = i + 1 });
                em.AddComponentData(e, new Health { Current = 100, Max = 200 });
            }

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
        }

        [Test]
        public void ValidChunkSection_Succeeds()
        {
            var data = SerializeWithChunks(3, 42);

            RestartWorld();
            var sys = CreateSystem();
            var load = sys.DeserializeAsync(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.None)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
        }
    }
}
