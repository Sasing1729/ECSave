using System.Linq;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// B-5 + P4: FixedString + Entity 混在 Chunk のラウンドトリップと P7.2 の相互作用。
    /// </summary>
    public unsafe class ChunkComponentMixedContentTests
    {
        private const string WorldName = "ChunkMixedWorld";
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World(WorldName);
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private SaveSystem CreateSystem(bool autoRepair = true)
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule(), new ChunkMixedModule() },
                new IdentityModule[] { new PlayerIdModule() },
                1, WorldName);

            if (!autoRepair)
                def.AutoRepairFixedString = false;

            return new SaveSystem(def);
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World(WorldName);
        }

        private static EntityQuery ChunkMixedQuery(EntityManager em)
            => em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ChunkComponentReadOnly<ChunkMixed>() },
            });

        private static ChunkMixed GetChunkValue(EntityManager em)
        {
            var q = ChunkMixedQuery(em);
            using var chunks = q.ToArchetypeChunkArray(Allocator.TempJob);
            Assert.AreEqual(1, chunks.Length);
            var entities = chunks[0].GetNativeArray(em.GetEntityTypeHandle());
            Assert.Greater(entities.Length, 0);
            return em.GetChunkComponentData<ChunkMixed>(entities[0]);
        }

        private static int CountWarnings(LoadResult load, LoadWarningKind kind)
        {
            int n = 0;
            for (int i = 0; i < load.Warnings.Count; i++)
                if (load.Warnings[i].Kind == kind) n++;
            return n;
        }

        private static unsafe void SetFixedStringRaw(
            ref FixedString32Bytes fs, byte[] data, int length)
        {
            byte* dst = (byte*)UnsafeUtility.AddressOf(ref fs);
            *(ushort*)dst = (ushort)length;
            for (int i = 0; i < length && i < data.Length; i++)
                dst[2 + i] = data[i];
        }

        private static byte[] RepeatA(int count)
        {
            var data = new byte[count * 3];
            for (int i = 0; i < count; i++)
            {
                data[i * 3 + 0] = 0xE3;
                data[i * 3 + 1] = 0x81;
                data[i * 3 + 2] = 0x82;
            }
            return data;
        }

        private static byte[] AWithPartialTail(int count, params byte[] partial)
        {
            var body = RepeatA(count);
            var result = new byte[body.Length + partial.Length];
            System.Buffer.BlockCopy(body, 0, result, 0, body.Length);
            System.Buffer.BlockCopy(partial, 0, result, body.Length, partial.Length);
            return result;
        }

        [Test]
        public void RoundTrip_FixedStringAndEntity_BothPreserved()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var target = em.CreateEntity();
            em.AddComponentData(target, new Health { Current = 100, Max = 200 });
            em.AddComponentData(target, new PlayerId { Value = 7 });

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddComponentData(holder, new PlayerId { Value = 1 });
            em.AddChunkComponentData<ChunkMixed>(holder);

            var chunk = em.GetChunk(holder);
            var value = new ChunkMixed
            {
                Target = target,
                Value = 42,
            };
            value.Name = "chunk-name";
            em.SetChunkComponentData(chunk, value);

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

            var em2 = _world.EntityManager;
            var restored = GetChunkValue(em2);

            Assert.AreEqual("chunk-name", restored.Name.ToString());
            Assert.AreEqual(42, restored.Value);
            Assert.AreNotEqual(Entity.Null, restored.Target);
            Assert.IsTrue(em2.HasComponent<PlayerId>(restored.Target));
            Assert.AreEqual(7, em2.GetComponentData<PlayerId>(restored.Target).Value);
            Assert.AreEqual(100, em2.GetComponentData<Health>(restored.Target).Current);
        }

        [Test]
        public void RoundTrip_NoReference_FixedStringStillWorks()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddComponentData(holder, new PlayerId { Value = 1 });
            em.AddChunkComponentData<ChunkMixed>(holder);

            var chunk = em.GetChunk(holder);
            var value = new ChunkMixed
            {
                Target = Entity.Null,
                Value = 99,
            };
            value.Name = "no-ref";
            em.SetChunkComponentData(chunk, value);

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            var restored = GetChunkValue(_world.EntityManager);
            Assert.AreEqual("no-ref", restored.Name.ToString());
            Assert.AreEqual(99, restored.Value);
            Assert.AreEqual(Entity.Null, restored.Target);
        }

        [Test]
        public void BrokenFixedString_Repaired_FixedStringWarningEmitted()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var target = em.CreateEntity();
            em.AddComponentData(target, new Health { Current = 50, Max = 100 });
            em.AddComponentData(target, new PlayerId { Value = 2 });

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddComponentData(holder, new PlayerId { Value = 1 });
            em.AddChunkComponentData<ChunkMixed>(holder);

            var chunk = em.GetChunk(holder);
            var broken = new ChunkMixed
            {
                Target = target,
                Value = 42,
            };
            SetFixedStringRaw(ref broken.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
            em.SetChunkComponentData(chunk, broken);

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

            Assert.AreEqual(1, CountWarnings(load, LoadWarningKind.FixedStringMayBeTruncated),
                "Broken FixedString should emit exactly one FixedStringMayBeTruncated warning");

            var restored = GetChunkValue(_world.EntityManager);
            Assert.AreEqual(27, restored.Name.Length,
                "FixedString should be truncated to the previous UTF-8 boundary");
            Assert.AreEqual("あああああああああ", restored.Name.ToString());
            Assert.AreEqual(42, restored.Value);
            Assert.AreNotEqual(Entity.Null, restored.Target,
                "Entity reference should still resolve despite FixedString repair");
        }

        [Test]
        public void BrokenFixedString_NoRepair_WarningOnly()
        {
            var sys = CreateSystem(autoRepair: false);
            var em = _world.EntityManager;

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddComponentData(holder, new PlayerId { Value = 1 });
            em.AddChunkComponentData<ChunkMixed>(holder);

            var chunk = em.GetChunk(holder);
            var broken = new ChunkMixed
            {
                Target = Entity.Null,
                Value = 7,
            };
            SetFixedStringRaw(ref broken.Name, AWithPartialTail(9, 0xE3, 0x81), 29);
            em.SetChunkComponentData(chunk, broken);

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);

            Assert.AreEqual(1, CountWarnings(load, LoadWarningKind.FixedStringMayBeTruncated));

            var restored = GetChunkValue(_world.EntityManager);
            Assert.AreEqual(29, restored.Name.Length,
                "Without auto-repair, the raw length should be preserved");
            Assert.AreEqual(7, restored.Value);
        }

        [Test]
        public void TamperedPid_WithFixedString_ReturnsEntityReferenceInvalid()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;

            var target = em.CreateEntity();
            em.AddComponentData(target, new Health { Current = 50, Max = 100 });
            em.AddComponentData(target, new PlayerId { Value = 3 });

            var holder = em.CreateEntity();
            em.AddComponentData(holder, new Health { Current = 10, Max = 20 });
            em.AddComponentData(holder, new PlayerId { Value = 1 });
            em.AddChunkComponentData<ChunkMixed>(holder);

            var chunk = em.GetChunk(holder);
            var value = new ChunkMixed
            {
                Target = target,
                Value = 42,
            };
            value.Name = "valid";
            em.SetChunkComponentData(chunk, value);

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            var err = SaveCompression.TryDecompress(save.Data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);

            int sectionOffset = FindChunkSectionOffset(raw);
            Assert.GreaterOrEqual(sectionOffset, 0);

            int tdSize = ReadTypeDescriptorSize(raw, sectionOffset);
            int bodyOffset = sectionOffset + tdSize;

            fixed (byte* p = raw)
            {
                byte* body = p + bodyOffset;
                uint groupCount = *(uint*)body;
                uint stride = *(uint*)(body + 8);

                // ChunkMixed: sizeof=44, refs=[32] → dataStride = 40
                Assert.AreEqual(40u, stride,
                    "dataStride should be rawSize(44) - 4 (Entity ref at offset 32)");

                byte* mo = body + 12;
                uint* moPtr = (uint*)mo;
                uint memberCount = moPtr[groupCount];
                byte* mei = mo + (groupCount + 1) * 4;
                byte* records = mei + (int)memberCount * 4;

                // pid は dataStride 上の offset 32（Name 0..32 の直後）
                *(uint*)(records + 32) = 0x00000100u;
            }
            RecomputeChecksum(raw);

            RestartWorld();

            var load = sys.DeserializeAsync(_world, raw,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.EntityReferenceInvalid, load.ErrorCode);
        }

        private static int FindChunkSectionOffset(byte[] data)
        {
            fixed (byte* p = data)
            {
                var view = new ECSave.Format.ByteView(p, data.Length);
                var reader = new ECSave.Format.FormatReader(view);
                reader.ReadHeader();
                var table = reader.ReadSectionTable();
                for (int i = 0; i < table.Count; i++)
                {
                    if (table.Entries[i].Kind == ECSave.Format.SectionKind.ChunkComponent)
                        return (int)table.Entries[i].Offset;
                }
            }
            return -1;
        }

        private static int ReadTypeDescriptorSize(byte[] data, int sectionOffset)
        {
            fixed (byte* p = data)
                return (int)(*(uint*)(p + sectionOffset));
        }

        private static void RecomputeChecksum(byte[] data)
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
}
