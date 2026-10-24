using System;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// Review #3: Identity セクションのスキーマ検証。
    /// D5 が Identity を skip していたため、SchemaHash / Stride が未検証だった。
    /// </summary>
    public unsafe class IdentitySchemaValidationTests
    {
        private const string WorldName = "IdentitySchemaTestWorld";

        // SectionEntry のフィールドオフセット（Runtime/Format/Model/SectionEntry.cs と整合）
        private const int EntrySectionIdOffset = 0;
        private const int EntryKindOffset = 4;
        private const int EntryFlagsOffset = 5;
        private const int EntryNameLengthOffset = 6;
        private const int EntryNameHashOffset = 8;
        private const int EntrySchemaHashOffset = 16;
        private const int EntryOffsetOffset = 24;
        private const int EntryLengthOffset = 32;
        private const int EntryNameOffsetOffset = 40;
        private const int EntryReservedOffset = 44;

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

        private SaveSystem CreateSystem(params IdentityModule[] ids)
        {
            if (ids == null || ids.Length == 0)
                ids = new IdentityModule[] { new PlayerIdModule() };

            var def = new SaveDefinition(
                new SaveTypeModule[] { new HealthModule() },
                ids, 1, WorldName);
            return new SaveSystem(def);
        }

        [Test]
        public void ValidIdentity_SchemaHashMatches_LoadsSuccessfully()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

            _world.Dispose();
            _world = new World(WorldName);

            var load = sys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();
            Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");
        }

        [Test]
        public void TamperedIdentitySchemaHash_ReturnsSchemaMismatch()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            var err = SaveCompression.TryDecompress(save.Data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);

            int entryOff = FindIdentityEntryOffset(raw);
            Assert.GreaterOrEqual(entryOff, 0);

            // entry[Identity].SchemaHash を改ざん
            fixed (byte* p = raw)
            {
                ulong* schemaHash = (ulong*)(p + entryOff + EntrySchemaHashOffset);
                *schemaHash = 0xDEADBEEFCAFEBABEUL;
            }
            RecomputeChecksum(raw);

            _world.Dispose();
            _world = new World(WorldName);

            var load = sys.DeserializeAsync(_world, raw,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.SchemaMismatch, load.ErrorCode);
        }

        [Test]
        public void TamperedIdentityStride_ReturnsSchemaMismatch()
        {
            var sys = CreateSystem();
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var save = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            var err = SaveCompression.TryDecompress(save.Data, out var raw);
            Assert.AreEqual(SaveLoadErrorCode.None, err);

            int entryOff = FindIdentityEntryOffset(raw);
            Assert.GreaterOrEqual(entryOff, 0);

            // entry[Identity].Offset を読み、body の Stride を改ざん
            ulong bodyOffset;
            fixed (byte* p = raw)
                bodyOffset = *(ulong*)(p + entryOff + EntryOffsetOffset);

            fixed (byte* p = raw)
            {
                // Type descriptor ブロック長（先頭 u32）を読み、body の先頭を求める
                uint tdSize = *(uint*)(p + bodyOffset);
                byte* body = p + bodyOffset + tdSize;

                // CommonSectionFormat: [Count u32][Flags u32][Stride u32]
                // Stride は KeyStride (4) と異なる値に改ざん
                *(uint*)(body + 8) = 8u;  // 4 ではなく 8
            }
            RecomputeChecksum(raw);

            _world.Dispose();
            _world = new World(WorldName);

            var load = sys.DeserializeAsync(_world, raw,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.SchemaMismatch, load.ErrorCode);
        }

        [Test]
        public void UnknownIdentity_EmitsUnknownSectionWarning()
        {
            // PlayerId で保存 → EnemyId のみ登録の SaveSystem でロード
            var saveSys = CreateSystem(new PlayerIdModule());
            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new PlayerId { Value = 1 });
            em.AddComponentData(e, new Health { Current = 42, Max = 100 });

            var save = saveSys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            _world.Dispose();
            _world = new World(WorldName);

            var loadSys = CreateSystem(new EnemyIdModule());
            var load = loadSys.DeserializeAsync(_world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess,
                "Unknown Identity should not fail, only warn");

            bool hasUnknown = false;
            for (int i = 0; i < load.Warnings.Count; i++)
                if (load.Warnings[i].Kind == LoadWarningKind.UnknownSectionIgnored)
                    hasUnknown = true;
            Assert.IsTrue(hasUnknown,
                "Unknown Identity should emit UnknownSectionIgnored");
        }

        /// <summary>
        /// Identity セクションの SectionEntry の絶対オフセットを返す。
        /// SectionTableOffset はヘッダから読む（ハードコード禁止）。
        /// </summary>
        private static int FindIdentityEntryOffset(byte[] data)
        {
            fixed (byte* p = data)
            {
                var view = new ByteView(p, data.Length);
                var reader = new FormatReader(view);
                var header = reader.ReadHeader();
                var table = reader.ReadSectionTable();

                long tableOffset = (long)header.SectionTableOffset;

                for (int i = 0; i < table.Count; i++)
                {
                    if (table.Entries[i].Kind == SectionKind.Identity)
                        return (int)(tableOffset + (long)i * ECSaveFormatConstants.SectionEntrySize);
                }
            }
            return -1;
        }

        private static void RecomputeChecksum(byte[] data)
        {
            fixed (byte* p = data)
            {
                int checksumEnd = data.Length - ECSaveFormatConstants.FooterSize;
                ulong ck = ChecksumCalculator.Compute(new ByteView(p, checksumEnd));
                *(ulong*)(p + checksumEnd) = ck;
            }
        }
    }
}
