using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace PidE2E
{
    // ═════════════════════════════════════════════════════════
    // FixedString スタブ（テスト専用）
    //
    // 実 Unity の FixedString32Bytes はバージョンにより内部レイアウトが
    // 異なる可能性があるため、テスト用に 32 バイト固定のスタブを用意する。
    // これにより、StructSize == RecordStride の関係が確定的になる。
    // ═════════════════════════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    public struct FixedString32Bytes
    {
        public ushort Length;
        public byte B0, B1, B2, B3, B4, B5, B6, B7, B8, B9,
                    B10, B11, B12, B13, B14, B15, B16, B17, B18, B19,
                    B20, B21, B22, B23, B24, B25, B26, B27, B28, B29;
    }
}

namespace ECSave.Tests.Runtime
{
    // ─────────────────────────────────────────
    // 既存テスト用の型（Entity + int）
    // ─────────────────────────────────────────

    public struct PidE2EHealthV1 : IComponentData
    {
        public Entity Owner;
        public int Value;
    }

    public struct PidE2EHealthV2 : IComponentData
    {
        public Entity Owner;
        public int Value;
        public float Multiplier;
    }

    public struct PidE2EHealthV1Pid
    {
        public uint Owner;
        public int Value;
    }

    public struct PidE2EHealthV2Pid
    {
        public uint Owner;
        public int Value;
        public float Multiplier;
    }

    public sealed class PidE2EHealthV1ToV2 : IPidMigration<PidE2EHealthV1Pid, PidE2EHealthV2Pid>
    {
        public PidE2EHealthV2Pid Migrate(in PidE2EHealthV1Pid old)
            => new PidE2EHealthV2Pid
            {
                Owner = old.Owner,
                Value = old.Value,
                Multiplier = 2.0f,
            };
    }

    // ─────────────────────────────────────────
    // Phase 3c: FixedString を含む型
    // ─────────────────────────────────────────

    public struct PidE2EHealthWithNameV1 : IComponentData
    {
        public Entity Owner;
        public PidE2E.FixedString32Bytes Name;
        public int Value;
    }

    public struct PidE2EHealthWithNameV2 : IComponentData
    {
        public Entity Owner;
        public PidE2E.FixedString32Bytes Name;
        public int Value;
        public float Multiplier;
    }

    public struct PidE2EHealthWithNameV1Pid
    {
        public uint Owner;
        public PidE2E.FixedString32Bytes Name;
        public int Value;
    }

    public struct PidE2EHealthWithNameV2Pid
    {
        public uint Owner;
        public PidE2E.FixedString32Bytes Name;
        public int Value;
        public float Multiplier;
    }

    public sealed class PidE2EHealthWithNameV1ToV2
        : IPidMigration<PidE2EHealthWithNameV1Pid, PidE2EHealthWithNameV2Pid>
    {
        public PidE2EHealthWithNameV2Pid Migrate(in PidE2EHealthWithNameV1Pid old)
            => new PidE2EHealthWithNameV2Pid
            {
                Owner = old.Owner,
                Name = old.Name,
                Value = old.Value,
                Multiplier = 3.0f,
            };
    }

    // ═════════════════════════════════════════════════════════
    // SchemaHashInput 構築ヘルパー
    // ═════════════════════════════════════════════════════════

    internal static class PidE2ESchemaHash
    {
        public static byte[] BuildInput(
            int structSize,
            params (SchemaHashKind kind, int size, int offset, ulong elementHash)[] fields)
        {
            var buf = new byte[2 + 16 * fields.Length];
            buf[0] = (byte)(structSize & 0xFF);
            buf[1] = (byte)((structSize >> 8) & 0xFF);
            int p = 2;
            foreach (var f in fields)
            {
                buf[p++] = (byte)f.kind;
                buf[p++] = (byte)(f.size & 0xFF);
                buf[p++] = (byte)((f.size >> 8) & 0xFF);
                buf[p++] = (byte)(f.offset & 0xFF);
                buf[p++] = (byte)((f.offset >> 8) & 0xFF);
                p += 3;
                for (int b = 0; b < 8; b++)
                    buf[p++] = (byte)((f.elementHash >> (b * 8)) & 0xFF);
            }
            return buf;
        }
    }

    // ═════════════════════════════════════════════════════════
    // モジュール
    // ═════════════════════════════════════════════════════════

    internal sealed class PidE2EHealthV1Module : SaveTypeModulePlain<PidE2EHealthV1>
    {
        private static readonly byte[] s_input = PidE2ESchemaHash.BuildInput(
            structSize: 12,
            (SchemaHashKind.Entity, 8, 0, 0UL),
            (SchemaHashKind.Int32, 4, 8, 0UL));
        private static readonly ulong s_hash = SchemaHashCore.Hash(s_input);

        public static ulong StaticSchemaHash => s_hash;
        public static byte[] StaticSchemaHashInput => s_input;

        public override ulong SchemaHash => s_hash;
        public override string FullTypeName => typeof(PidE2EHealthV1).FullName;
        public override byte[] SchemaHashInput => s_input;
        public override string[] FieldNames => new[] { "Owner", "Value" };
        public override int[] EntityRefOffsets => new[] { 0 };

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            var v = default(PidE2EHealthV1);
            UnsafeUtility.MemCpy(
                UnsafeUtility.AddressOf(ref v), value,
                UnsafeUtility.SizeOf<PidE2EHealthV1>());
            if (em.HasComponent<PidE2EHealthV1>(entity))
                em.SetComponentData(entity, v);
            else
                em.AddComponentData(entity, v);
        }
    }

    internal sealed class PidE2EHealthV2Module : SaveTypeModulePlain<PidE2EHealthV2>
    {
        private static readonly byte[] s_input = PidE2ESchemaHash.BuildInput(
            structSize: 16,
            (SchemaHashKind.Entity, 8, 0, 0UL),
            (SchemaHashKind.Int32, 4, 8, 0UL),
            (SchemaHashKind.Float32, 4, 12, 0UL));
        private static readonly ulong s_hash = SchemaHashCore.Hash(s_input);

        public static ulong StaticSchemaHash => s_hash;
        public static byte[] StaticSchemaHashInput => s_input;

        public override ulong SchemaHash => s_hash;
        public override string FullTypeName => typeof(PidE2EHealthV2).FullName;
        public override byte[] SchemaHashInput => s_input;
        public override string[] FieldNames => new[] { "Owner", "Value", "Multiplier" };
        public override int[] EntityRefOffsets => new[] { 0 };

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            var v = default(PidE2EHealthV2);
            UnsafeUtility.MemCpy(
                UnsafeUtility.AddressOf(ref v), value,
                UnsafeUtility.SizeOf<PidE2EHealthV2>());
            if (em.HasComponent<PidE2EHealthV2>(entity))
                em.SetComponentData(entity, v);
            else
                em.AddComponentData(entity, v);
        }
    }

    // ── FixedString を含むモジュール ──

    internal sealed class PidE2EHealthWithNameV1Module : SaveTypeModulePlain<PidE2EHealthWithNameV1>
    {
        // PidE2EHealthWithNameV1 raw layout:
        //   Entity Owner (8B) at offset 0
        //   FixedString32Bytes Name (32B) at offset 8
        //   int Value (4B) at offset 40
        //   StructSize = 44
        // ElementHash for FixedString32Bytes = HashUtf8("Unity.Collections.FixedString32Bytes")
        private static readonly ulong s_fixedStringHash =
            SchemaHashCore.HashUtf8("Unity.Collections.FixedString32Bytes");

        private static readonly byte[] s_input = PidE2ESchemaHash.BuildInput(
            structSize: 44,
            (SchemaHashKind.Entity, 8, 0, 0UL),
            (SchemaHashKind.FixedString, 32, 8, s_fixedStringHash),
            (SchemaHashKind.Int32, 4, 40, 0UL));
        private static readonly ulong s_hash = SchemaHashCore.Hash(s_input);

        public static ulong StaticSchemaHash => s_hash;
        public static byte[] StaticSchemaHashInput => s_input;

        public override ulong SchemaHash => s_hash;
        public override string FullTypeName => typeof(PidE2EHealthWithNameV1).FullName;
        public override byte[] SchemaHashInput => s_input;
        public override string[] FieldNames => new[] { "Owner", "Name", "Value" };
        public override int[] EntityRefOffsets => new[] { 0 };

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            var v = default(PidE2EHealthWithNameV1);
            UnsafeUtility.MemCpy(
                UnsafeUtility.AddressOf(ref v), value,
                UnsafeUtility.SizeOf<PidE2EHealthWithNameV1>());
            if (em.HasComponent<PidE2EHealthWithNameV1>(entity))
                em.SetComponentData(entity, v);
            else
                em.AddComponentData(entity, v);
        }
    }

    internal sealed class PidE2EHealthWithNameV2Module : SaveTypeModulePlain<PidE2EHealthWithNameV2>
    {
        // PidE2EHealthWithNameV2 raw layout:
        //   Entity Owner (8B) at 0
        //   FixedString32Bytes Name (32B) at 8
        //   int Value (4B) at 40
        //   float Multiplier (4B) at 44
        //   StructSize = 48
        private static readonly ulong s_fixedStringHash =
            SchemaHashCore.HashUtf8("Unity.Collections.FixedString32Bytes");

        private static readonly byte[] s_input = PidE2ESchemaHash.BuildInput(
            structSize: 48,
            (SchemaHashKind.Entity, 8, 0, 0UL),
            (SchemaHashKind.FixedString, 32, 8, s_fixedStringHash),
            (SchemaHashKind.Int32, 4, 40, 0UL),
            (SchemaHashKind.Float32, 4, 44, 0UL));
        private static readonly ulong s_hash = SchemaHashCore.Hash(s_input);

        public static ulong StaticSchemaHash => s_hash;
        public static byte[] StaticSchemaHashInput => s_input;

        public override ulong SchemaHash => s_hash;
        public override string FullTypeName => typeof(PidE2EHealthWithNameV2).FullName;
        public override byte[] SchemaHashInput => s_input;
        public override string[] FieldNames => new[] { "Owner", "Name", "Value", "Multiplier" };
        public override int[] EntityRefOffsets => new[] { 0 };

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
        {
            var v = default(PidE2EHealthWithNameV2);
            UnsafeUtility.MemCpy(
                UnsafeUtility.AddressOf(ref v), value,
                UnsafeUtility.SizeOf<PidE2EHealthWithNameV2>());
            if (em.HasComponent<PidE2EHealthWithNameV2>(entity))
                em.SetComponentData(entity, v);
            else
                em.AddComponentData(entity, v);
        }
    }

    // ═════════════════════════════════════════════════════════
    // Step クラス（SG 生成コードと等価）
    // ═════════════════════════════════════════════════════════

    internal sealed class PidE2EHealthV1ToV2Step : MigrationStepBase
    {
        private readonly PidE2EHealthV1ToV2 _impl = new PidE2EHealthV1ToV2();

        public override string NewTypeName => typeof(PidE2EHealthV2).FullName;
        public override ulong NewTypeNameHash => SchemaHashCore.HashUtf8(NewTypeName);
        public override ulong OldSchemaHash => PidE2EHealthV1Module.StaticSchemaHash;
        public override ulong NewSchemaHash => PidE2EHealthV2Module.StaticSchemaHash;
        public override int NewRecordStride => UnsafeUtility.SizeOf<PidE2EHealthV2Pid>();

        public override unsafe void Migrate(ref ByteReader reader, ref ByteWriter writer)
        {
            int oldSize = UnsafeUtility.SizeOf<PidE2EHealthV1Pid>();
            int newSize = UnsafeUtility.SizeOf<PidE2EHealthV2Pid>();

            if (reader.Remaining != oldSize)
                throw new InvalidOperationException("Migration record size mismatch.");

            var old = default(PidE2EHealthV1Pid);
            UnsafeUtility.MemCpy(&old, reader.ReadBytesPtr(oldSize), oldSize);

            var @new = _impl.Migrate(in old);

            writer.WriteBytes((byte*)&@new, newSize);
        }
    }

    internal sealed class PidE2EHealthWithNameV1ToV2Step : MigrationStepBase
    {
        private readonly PidE2EHealthWithNameV1ToV2 _impl = new PidE2EHealthWithNameV1ToV2();

        public override string NewTypeName => typeof(PidE2EHealthWithNameV2).FullName;
        public override ulong NewTypeNameHash => SchemaHashCore.HashUtf8(NewTypeName);
        public override ulong OldSchemaHash => PidE2EHealthWithNameV1Module.StaticSchemaHash;
        public override ulong NewSchemaHash => PidE2EHealthWithNameV2Module.StaticSchemaHash;
        public override int NewRecordStride => UnsafeUtility.SizeOf<PidE2EHealthWithNameV2Pid>();

        public override unsafe void Migrate(ref ByteReader reader, ref ByteWriter writer)
        {
            int oldSize = UnsafeUtility.SizeOf<PidE2EHealthWithNameV1Pid>();
            int newSize = UnsafeUtility.SizeOf<PidE2EHealthWithNameV2Pid>();

            if (reader.Remaining != oldSize)
                throw new InvalidOperationException("Migration record size mismatch.");

            var old = default(PidE2EHealthWithNameV1Pid);
            UnsafeUtility.MemCpy(&old, reader.ReadBytesPtr(oldSize), oldSize);

            var @new = _impl.Migrate(in old);

            writer.WriteBytes((byte*)&@new, newSize);
        }
    }

    // ═════════════════════════════════════════════════════════
    // E2E テスト
    // ═════════════════════════════════════════════════════════

    [TestFixture]
    public sealed class PidMigrationE2ETests
    {
        private const string WorldName = "PidMigrationE2EWorld";
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

        private void RestartWorld()
        {
            _world.Dispose();
            _world = new World(WorldName);
        }

        // ─────────────────────────────────────────
        // 既存 5 テスト（省略：前回出力分と同じ）
        // ─────────────────────────────────────────

        [Test]
        public void SaveV1_LoadV2_MigratesAndPreservesEntity()
        {
            var saveSys = CreateSaveSystem();
            var em = _world.EntityManager;
            var entity = em.CreateEntity();
            em.AddComponentData(entity, new PidE2EHealthV1 { Owner = entity, Value = 42 });

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            RestartWorld();
            var loadSys = CreateLoadSystem();
            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(1, load.EntityCount);

            var q = _world.EntityManager.CreateEntityQuery(typeof(PidE2EHealthV2));
            using var vals = q.ToComponentDataArray<PidE2EHealthV2>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(42, vals[0].Value);
            Assert.AreEqual(2.0f, vals[0].Multiplier);
            Assert.AreNotEqual(Entity.Null, vals[0].Owner);
        }

        [Test]
        public void SaveV1_TwoEntitiesWithCrossReference_LoadV2_PreservesRefs()
        {
            var saveSys = CreateSaveSystem();
            var em = _world.EntityManager;
            var a = em.CreateEntity();
            var b = em.CreateEntity();
            em.AddComponentData(a, new PidE2EHealthV1 { Owner = b, Value = 1 });
            em.AddComponentData(b, new PidE2EHealthV1 { Owner = a, Value = 2 });

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var loadSys = CreateLoadSystem();
            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);
            Assert.AreEqual(2, load.EntityCount);

            var q = _world.EntityManager.CreateEntityQuery(typeof(PidE2EHealthV2));
            using var entities = q.ToEntityArray(Allocator.Temp);
            using var vals = q.ToComponentDataArray<PidE2EHealthV2>(Allocator.Temp);

            PidE2EHealthV2 va = default, vb = default;
            Entity ea = Entity.Null, eb = Entity.Null;
            for (int i = 0; i < vals.Length; i++)
            {
                if (vals[i].Value == 1) { va = vals[i]; ea = entities[i]; }
                else if (vals[i].Value == 2) { vb = vals[i]; eb = entities[i]; }
            }

            Assert.AreEqual(eb, va.Owner);
            Assert.AreEqual(ea, vb.Owner);
        }

        [Test]
        public void LoadV2_WithoutMigration_ReturnsSchemaMismatch()
        {
            var saveSys = CreateSaveSystem();
            var em = _world.EntityManager;
            var entity = em.CreateEntity();
            em.AddComponentData(entity, new PidE2EHealthV1 { Owner = entity, Value = 1 });

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var table = new MigrationTable();
            table.RegisterRenamed(
                SchemaHashCore.HashUtf8(typeof(PidE2EHealthV1).FullName),
                SchemaHashCore.HashUtf8(typeof(PidE2EHealthV2).FullName),
                typeof(PidE2EHealthV1).FullName,
                typeof(PidE2EHealthV2).FullName);

            var loadSys = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new PidE2EHealthV2Module() },
                gameDataVersion: 1, worldId: WorldName)
            {
                Migrations = table,
            });

            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.SchemaMismatch, load.ErrorCode);
        }

        [Test]
        public void SaveV1_WithNullOwner_LoadV2_Succeeds()
        {
            var saveSys = CreateSaveSystem();
            var em = _world.EntityManager;
            var entity = em.CreateEntity();
            em.AddComponentData(entity, new PidE2EHealthV1 { Owner = Entity.Null, Value = 7 });

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var loadSys = CreateLoadSystem();
            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);

            var q = _world.EntityManager.CreateEntityQuery(typeof(PidE2EHealthV2));
            using var vals = q.ToComponentDataArray<PidE2EHealthV2>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(7, vals[0].Value);
            Assert.AreEqual(2.0f, vals[0].Multiplier);
            Assert.AreEqual(Entity.Null, vals[0].Owner);
        }

        [Test]
        public void SaveV1_EmptyWorld_LoadV2_Succeeds()
        {
            var saveSys = CreateSaveSystem();
            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var loadSys = CreateLoadSystem();
            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);
            Assert.AreEqual(0, load.EntityCount);
        }

        // ═══════════════════════════════════════════
        // Phase 3c: FixedString E2E
        // ═══════════════════════════════════════════

        [Test]
        public void SaveV1_WithFixedString_LoadV2_Migrates()
        {
            // レイアウト検証（前提条件）
            Assert.AreEqual(44, UnsafeUtility.SizeOf<PidE2EHealthWithNameV1>(),
                "V1 must be 44 bytes (8 + 32 + 4)");
            Assert.AreEqual(40, UnsafeUtility.SizeOf<PidE2EHealthWithNameV1Pid>(),
                "V1Pid must be 40 bytes (4 + 32 + 4)");
            Assert.AreEqual(48, UnsafeUtility.SizeOf<PidE2EHealthWithNameV2>(),
                "V2 must be 48 bytes (8 + 32 + 4 + 4)");
            Assert.AreEqual(44, UnsafeUtility.SizeOf<PidE2EHealthWithNameV2Pid>(),
                "V2Pid must be 44 bytes (4 + 32 + 4 + 4)");

            // 1. 保存: V1 で FixedString + Entity + int を持つ
            var saveSys = CreateSaveWithNameSystem();
            var em = _world.EntityManager;
            var entity = em.CreateEntity();

            var v1 = new PidE2EHealthWithNameV1
            {
                Owner = entity,
                Value = 42,
            };
            v1.Name.Length = 5;
            v1.Name.B0 = (byte)'H';
            v1.Name.B1 = (byte)'e';
            v1.Name.B2 = (byte)'l';
            v1.Name.B3 = (byte)'l';
            v1.Name.B4 = (byte)'o';
            em.AddComponentData(entity, v1);

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize failed: {save.ErrorCode}");

            // 2. 別ワールドで V2 + Migration でロード
            RestartWorld();
            var loadSys = CreateLoadWithNameSystem();
            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(1, load.EntityCount);

            // 3. V2 コンポーネント復元
            var q = _world.EntityManager.CreateEntityQuery(typeof(PidE2EHealthWithNameV2));
            using var vals = q.ToComponentDataArray<PidE2EHealthWithNameV2>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);

            var v2 = vals[0];
            Assert.AreEqual(42, v2.Value, "Value preserved");
            Assert.AreEqual(3.0f, v2.Multiplier, "Multiplier from migration");
            Assert.AreNotEqual(Entity.Null, v2.Owner, "Owner Entity preserved");

            // FixedString の内容保持
            Assert.AreEqual(5, v2.Name.Length, "FixedString length preserved");
            Assert.AreEqual((byte)'H', v2.Name.B0);
            Assert.AreEqual((byte)'e', v2.Name.B1);
            Assert.AreEqual((byte)'l', v2.Name.B2);
            Assert.AreEqual((byte)'l', v2.Name.B3);
            Assert.AreEqual((byte)'o', v2.Name.B4);
        }

        [Test]
        public void SaveV1_WithFixedString_TwoEntities_PreservesEach()
        {
            var saveSys = CreateSaveWithNameSystem();
            var em = _world.EntityManager;

            var a = em.CreateEntity();
            var va = new PidE2EHealthWithNameV1 { Owner = a, Value = 10 };
            va.Name.Length = 1;
            va.Name.B0 = (byte)'A';
            em.AddComponentData(a, va);

            var b = em.CreateEntity();
            var vb = new PidE2EHealthWithNameV1 { Owner = b, Value = 20 };
            vb.Name.Length = 1;
            vb.Name.B0 = (byte)'B';
            em.AddComponentData(b, vb);

            var save = saveSys.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess);

            RestartWorld();
            var loadSys = CreateLoadWithNameSystem();
            var load = loadSys.Deserialize(
                _world, save.Data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration).GetAwaiter().GetResult();

            Assert.IsTrue(load.IsSuccess);
            Assert.AreEqual(2, load.EntityCount);

            var q = _world.EntityManager.CreateEntityQuery(typeof(PidE2EHealthWithNameV2));
            using var vals = q.ToComponentDataArray<PidE2EHealthWithNameV2>(Allocator.Temp);
            Assert.AreEqual(2, vals.Length);

            PidE2EHealthWithNameV2 ra = default, rb = default;
            for (int i = 0; i < vals.Length; i++)
            {
                if (vals[i].Value == 10) ra = vals[i];
                else if (vals[i].Value == 20) rb = vals[i];
            }

            Assert.AreEqual(1, ra.Name.Length);
            Assert.AreEqual((byte)'A', ra.Name.B0);
            Assert.AreEqual(1, rb.Name.Length);
            Assert.AreEqual((byte)'B', rb.Name.B0);
        }

        // ─────────────────────────────────────────
        // ヘルパー
        // ─────────────────────────────────────────

        private static SaveSystem CreateSaveSystem()
            => new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new PidE2EHealthV1Module() },
                gameDataVersion: 1, worldId: WorldName));

        private static SaveSystem CreateLoadSystem()
        {
            var table = new MigrationTable();
            table.RegisterRenamed(
                SchemaHashCore.HashUtf8(typeof(PidE2EHealthV1).FullName),
                SchemaHashCore.HashUtf8(typeof(PidE2EHealthV2).FullName),
                typeof(PidE2EHealthV1).FullName,
                typeof(PidE2EHealthV2).FullName);
            table.Register(new PidE2EHealthV1ToV2Step());

            return new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new PidE2EHealthV2Module() },
                gameDataVersion: 1, worldId: WorldName)
            {
                Migrations = table,
            });
        }

        private static SaveSystem CreateSaveWithNameSystem()
            => new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new PidE2EHealthWithNameV1Module() },
                gameDataVersion: 1, worldId: WorldName));

        private static SaveSystem CreateLoadWithNameSystem()
        {
            var table = new MigrationTable();
            table.RegisterRenamed(
                SchemaHashCore.HashUtf8(typeof(PidE2EHealthWithNameV1).FullName),
                SchemaHashCore.HashUtf8(typeof(PidE2EHealthWithNameV2).FullName),
                typeof(PidE2EHealthWithNameV1).FullName,
                typeof(PidE2EHealthWithNameV2).FullName);
            table.Register(new PidE2EHealthWithNameV1ToV2Step());

            return new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new PidE2EHealthWithNameV2Module() },
                gameDataVersion: 1, worldId: WorldName)
            {
                Migrations = table,
            });
        }
    }
}
