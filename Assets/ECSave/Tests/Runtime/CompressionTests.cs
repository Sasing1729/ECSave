using System;
using System.Text;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    public unsafe class CompressionTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("CompressionTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        private static SaveSystem CreateSystem()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "CompressionTestWorld");
            return new SaveSystem(def);
        }

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World("CompressionTestWorld");
        }

        private static byte[] RandomBytes(int length, int seed)
        {
            var b = new byte[length];
            new Random(seed).NextBytes(b);
            return b;
        }

        private static void Lz4RoundTrip(byte[] src)
        {
            int bound = Lz4Block.MaxCompressedSize(src.Length);
            var comp = new byte[bound];
            var restored = new byte[src.Length];
            int n;
            bool ok;

            fixed (byte* s = src)
            fixed (byte* c = comp)
            {
                n = Lz4Block.Compress(s, src.Length, c, bound);
            }

            Assert.Greater(n, 0, "compress failed");

            fixed (byte* c = comp)
            fixed (byte* o = restored)
            {
                ok = Lz4Block.Decompress(c, n, o, src.Length);
            }

            Assert.IsTrue(ok, $"decompress failed (len={src.Length})");
            CollectionAssert.AreEqual(src, restored, $"mismatch (len={src.Length})");
        }

        private static byte[] CompressibleEnvelope(int rawLength = 10000)
        {
            var comp = SaveCompression.Compress(new byte[rawLength]);
            Assert.IsTrue(SaveCompression.IsCompressed(comp));
            return comp;
        }

        private byte[] SerializeHealthWorld(SaveSystem sys, int count, out long sum)
        {
            var em = _world.EntityManager;
            var archetype = em.CreateArchetype(typeof(Health));
            using var entities = em.CreateEntity(archetype, count, Allocator.Temp);
            sum = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                int cur = i % 7;
                sum += cur;
                em.SetComponentData(entities[i], new Health { Current = cur, Max = 1000 });
            }

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");
            return save.Data;
        }

        [Test]
        public void Lz4_SmallSizes_Random_RoundTrip()
        {
            for (int n = 1; n <= 200; n++)
                Lz4RoundTrip(RandomBytes(n, n));
        }

        [Test]
        public void Lz4_SmallSizes_Zeros_RoundTrip()
        {
            for (int n = 1; n <= 200; n++)
                Lz4RoundTrip(new byte[n]);
        }

        [Test]
        public void Lz4_LongZeros_RoundTrip()
        {
            Lz4RoundTrip(new byte[100000]);
        }

        [Test]
        public void Lz4_LargeRandom_RoundTrip()
        {
            Lz4RoundTrip(RandomBytes(100000, 7));
        }

        [Test]
        public void Lz4_OverlappingMatch_Period3_RoundTrip()
        {
            var b = new byte[5000];
            for (int i = 0; i < b.Length; i++) b[i] = (byte)(i % 3);
            Lz4RoundTrip(b);
        }

        [Test]
        public void Lz4_OverlappingMatch_Period1_RoundTrip()
        {
            var b = new byte[3000];
            for (int i = 0; i < b.Length; i++) b[i] = 0x55;
            Lz4RoundTrip(b);
        }

        [Test]
        public void Lz4_MatchBeyondMaxOffset_RoundTrip()
        {
            var head = RandomBytes(70000, 11);
            var b = new byte[head.Length + 2000];
            Array.Copy(head, b, head.Length);
            Array.Copy(head, 0, b, head.Length, 2000);
            Lz4RoundTrip(b);
        }

        [Test]
        public void Lz4_TextLike_RoundTrip()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 500; i++) sb.Append("ECSave LZ4 test ").Append(i % 13).Append(';');
            Lz4RoundTrip(Encoding.ASCII.GetBytes(sb.ToString()));
        }

        [Test]
        public void Lz4_MixedLiteralAndMatch_RoundTrip()
        {
            var rng = new Random(3);
            var b = new byte[20000];
            int i = 0;
            while (i < b.Length)
            {
                int run = rng.Next(1, 300);
                bool repeat = rng.Next(2) == 0 && i > 0;
                for (int k = 0; k < run && i < b.Length; k++, i++)
                    b[i] = repeat ? b[i - 1] : (byte)rng.Next(256);
            }
            Lz4RoundTrip(b);
        }

        [Test]
        public void Lz4_Decompress_WrongOutputLength_Fails()
        {
            var src = new byte[1000];
            int bound = Lz4Block.MaxCompressedSize(src.Length);
            var comp = new byte[bound];
            int n;
            fixed (byte* s = src)
            fixed (byte* c = comp)
                n = Lz4Block.Compress(s, src.Length, c, bound);

            var smaller = new byte[999];
            var larger = new byte[1001];
            bool okSmaller, okLarger;
            fixed (byte* c = comp)
            fixed (byte* o = smaller)
                okSmaller = Lz4Block.Decompress(c, n, o, smaller.Length);
            fixed (byte* c = comp)
            fixed (byte* o = larger)
                okLarger = Lz4Block.Decompress(c, n, o, larger.Length);

            Assert.IsFalse(okSmaller);
            Assert.IsFalse(okLarger);
        }

        [Test]
        public void Lz4_Decompress_Truncated_Fails()
        {
            var src = RandomBytes(500, 5);
            for (int i = 100; i < 400; i++) src[i] = 0;
            int bound = Lz4Block.MaxCompressedSize(src.Length);
            var comp = new byte[bound];
            int n;
            fixed (byte* s = src)
            fixed (byte* c = comp)
                n = Lz4Block.Compress(s, src.Length, c, bound);

            var outp = new byte[src.Length];
            bool ok;
            fixed (byte* c = comp)
            fixed (byte* o = outp)
                ok = Lz4Block.Decompress(c, n - 1, o, outp.Length);

            Assert.IsFalse(ok);
        }

        [Test]
        public void Lz4_Decompress_ZeroOffset_Fails()
        {
            var stream = new byte[] { 0x10, 0xAA, 0x00, 0x00 };
            var outp = new byte[16];
            bool ok;
            fixed (byte* c = stream)
            fixed (byte* o = outp)
                ok = Lz4Block.Decompress(c, stream.Length, o, outp.Length);
            Assert.IsFalse(ok);
        }

        [Test]
        public void Lz4_Decompress_OffsetBeyondOutput_Fails()
        {
            var stream = new byte[] { 0x10, 0xAA, 0x05, 0x00 };
            var outp = new byte[16];
            bool ok;
            fixed (byte* c = stream)
            fixed (byte* o = outp)
                ok = Lz4Block.Decompress(c, stream.Length, o, outp.Length);
            Assert.IsFalse(ok);
        }

        [Test]
        public void Envelope_Null_ReturnsNull()
        {
            Assert.IsNull(SaveCompression.Compress(null));
        }

        [Test]
        public void Envelope_SmallInput_ReturnedAsIs()
        {
            var raw = new byte[SaveCompression.MinCompressLength - 1];
            Assert.AreSame(raw, SaveCompression.Compress(raw));
        }

        [Test]
        public void Envelope_Incompressible_ReturnedAsIs()
        {
            var raw = RandomBytes(2000, 1);
            var result = SaveCompression.Compress(raw);
            Assert.AreSame(raw, result);
            Assert.IsFalse(SaveCompression.IsCompressed(result));
        }

        [Test]
        public void Envelope_Compressible_IsSmallerAndRestores()
        {
            var raw = new byte[10000];
            for (int i = 0; i < raw.Length; i++) raw[i] = (byte)(i % 5);

            var comp = SaveCompression.Compress(raw);
            Assert.IsTrue(SaveCompression.IsCompressed(comp));
            Assert.Less(comp.Length, raw.Length);

            var err = SaveCompression.TryDecompress(comp, out var restored);
            Assert.AreEqual(SaveLoadErrorCode.None, err);
            CollectionAssert.AreEqual(raw, restored);
        }

        [Test]
        public void Envelope_TryDecompress_NotCompressed_PassThrough()
        {
            var raw = RandomBytes(100, 2);
            var err = SaveCompression.TryDecompress(raw, out var result);
            Assert.AreEqual(SaveLoadErrorCode.None, err);
            Assert.AreSame(raw, result);
        }

        [Test]
        public void Envelope_UnknownCodec_VersionMismatch()
        {
            var comp = CompressibleEnvelope();
            comp[4] = 2;
            Assert.AreEqual(SaveLoadErrorCode.VersionMismatch,
                SaveCompression.TryReadRawLength(comp, out _));
        }

        [Test]
        public void Envelope_NonZeroFlags_CorruptedData()
        {
            var comp = CompressibleEnvelope();
            comp[5] = 1;
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData,
                SaveCompression.TryReadRawLength(comp, out _));

            comp = CompressibleEnvelope();
            comp[6] = 1;
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData,
                SaveCompression.TryReadRawLength(comp, out _));

            comp = CompressibleEnvelope();
            comp[7] = 1;
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData,
                SaveCompression.TryReadRawLength(comp, out _));
        }

        [Test]
        public void Envelope_RawLengthTooSmall_CorruptedData()
        {
            var comp = CompressibleEnvelope();
            comp[8] = 10; comp[9] = 0; comp[10] = 0; comp[11] = 0;
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData,
                SaveCompression.TryReadRawLength(comp, out _));
        }

        [Test]
        public void Envelope_RawLengthDecompressionBomb_CorruptedData()
        {
            var comp = CompressibleEnvelope();
            comp[8] = 0xFF; comp[9] = 0xFF; comp[10] = 0xFF; comp[11] = 0x7F;
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData,
                SaveCompression.TryReadRawLength(comp, out _));
        }

        [Test]
        public void Envelope_RawLengthMismatch_TryDecompressFails()
        {
            var comp = CompressibleEnvelope(10000);

            var smaller = (byte[])comp.Clone();
            uint s = 9999;
            smaller[8] = (byte)s; smaller[9] = (byte)(s >> 8);
            smaller[10] = (byte)(s >> 16); smaller[11] = (byte)(s >> 24);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData,
                SaveCompression.TryDecompress(smaller, out _));

            var larger = (byte[])comp.Clone();
            uint l = 10001;
            larger[8] = (byte)l; larger[9] = (byte)(l >> 8);
            larger[10] = (byte)(l >> 16); larger[11] = (byte)(l >> 24);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData,
                SaveCompression.TryDecompress(larger, out _));
        }

        [Test]
        public void RoundTrip_LargeWorld_IsCompressedAndRestored()
        {
            var sys = CreateSystem();
            const int count = 5000;
            var data = SerializeHealthWorld(sys, count, out long expectedSum);

            Assert.IsTrue(SaveCompression.IsCompressed(data), "expected compressed envelope");

            SaveCompression.TryDecompress(data, out var raw);
            Assert.Less(data.Length, raw.Length);

            RestartWorld();

            var load = sys.Deserialize(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(count, load.EntityCount);

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(count, vals.Length);

            long sum = 0;
            for (int i = 0; i < vals.Length; i++)
            {
                sum += vals[i].Current;
                Assert.AreEqual(1000, vals[i].Max);
            }
            Assert.AreEqual(expectedSum, sum);
        }

        [Test]
        public void RoundTrip_NonBlocking_IsCompressedAndRestored()
        {
            var sys = CreateSystem();
            const int count = 3000;
            var data = SerializeHealthWorld(sys, count, out _);
            Assert.IsTrue(SaveCompression.IsCompressed(data));

            RestartWorld();

            var load = sys.Deserialize(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess);
            Assert.AreEqual(count, load.EntityCount);
        }

        [Test]
        public void Deserialize_UncompressedData_StillLoads()
        {
            var sys = CreateSystem();
            const int count = 2000;
            var data = SerializeHealthWorld(sys, count, out _);

            var err = SaveCompression.TryDecompress(data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);
            Assert.IsFalse(SaveCompression.IsCompressed(raw));

            RestartWorld();

            var load = sys.Deserialize(_world, raw,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(count, load.EntityCount);
        }

        [Test]
        public void Deserialize_SmallWorld_NotCompressed_StillLoads()
        {
            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                gameDataVersion: 1,
                worldId: "W");
            var sys = new SaveSystem(def);

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = 5, Max = 9 });

            var save = sys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");
            Assert.Less(save.Data.Length, SaveCompression.MinCompressLength);
            Assert.IsFalse(SaveCompression.IsCompressed(save.Data));

            RestartWorld();

            var load = sys.Deserialize(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(1, load.EntityCount);

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(5, vals[0].Current);
            Assert.AreEqual(9, vals[0].Max);
        }

        [Test]
        public void Deserialize_CorruptedCompressedBody_Fails()
        {
            var sys = CreateSystem();
            var data = SerializeHealthWorld(sys, 5000, out _);
            Assert.IsTrue(SaveCompression.IsCompressed(data));

            int mid = SaveCompression.EnvelopeSize + (data.Length - SaveCompression.EnvelopeSize) / 2;
            data[mid] ^= 0x01;

            RestartWorld();

            var load = sys.Deserialize(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.IsTrue(
                load.ErrorCode == SaveLoadErrorCode.CorruptedData
                || load.ErrorCode == SaveLoadErrorCode.ChecksumMismatch,
                $"unexpected error: {load.ErrorCode}");
        }

        [Test]
        public void Deserialize_TruncatedCompressedData_CorruptedData()
        {
            var sys = CreateSystem();
            var data = SerializeHealthWorld(sys, 5000, out _);
            Assert.IsTrue(SaveCompression.IsCompressed(data));

            var truncated = new byte[data.Length - 10];
            Array.Copy(data, truncated, truncated.Length);

            RestartWorld();

            var load = sys.Deserialize(_world, truncated,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, load.ErrorCode);
        }

        [Test]
        public void Deserialize_BombRawLength_CorruptedData()
        {
            var sys = CreateSystem();
            var data = SerializeHealthWorld(sys, 5000, out _);
            Assert.IsTrue(SaveCompression.IsCompressed(data));

            data[8] = 0xFF; data[9] = 0xFF; data[10] = 0xFF; data[11] = 0x7F;

            RestartWorld();

            var load = sys.Deserialize(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.CorruptedData, load.ErrorCode);
        }

        [Test]
        public void Deserialize_UnknownCodec_VersionMismatch()
        {
            var sys = CreateSystem();
            var data = SerializeHealthWorld(sys, 5000, out _);
            Assert.IsTrue(SaveCompression.IsCompressed(data));

            data[4] = 9;

            RestartWorld();

            var load = sys.Deserialize(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.VersionMismatch, load.ErrorCode);
        }

        [Test]
        public void Deserialize_InnerChecksumTampered_ChecksumMismatch()
        {
            var sys = CreateSystem();
            var data = SerializeHealthWorld(sys, 5000, out _);

            SaveCompression.TryDecompress(data, out var raw);
            int mid = raw.Length / 2;
            raw[mid] ^= 0x01;

            var recompressed = SaveCompression.Compress(raw);

            RestartWorld();

            var load = sys.Deserialize(_world, recompressed,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.ChecksumMismatch, load.ErrorCode);
        }
    }
}
