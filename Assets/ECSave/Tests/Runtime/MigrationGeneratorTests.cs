using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using NUnit.Framework;
using System.Collections;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace ECSave.Tests.Generator
{
    public struct GenPadV1 : IComponentData
    {
        public int A;
        public long B;
    }

    public struct GenPadV2 : IComponentData
    {
        public int A;
        public long B;
        public int C;
    }

    public sealed class GenPadMigration : IComponentMigration<GenPadV1, GenPadV2>
    {
        public GenPadV2 Migrate(in GenPadV1 old)
            => new GenPadV2 { A = old.A, B = old.B, C = old.A + 1 };
    }

    public sealed class GenHealthV1ToHealth : IComponentMigration<HealthV1, Health>
    {
        public Health Migrate(in HealthV1 old)
            => new Health { Current = old.Value, Max = old.Value * 2 };
    }

    public sealed class GenPadMigrationScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<GenPadV2>();
            b.RegisterMigration<GenPadMigration>();
            b.WorldId("GenPadMigrationWorld");
        }
    }

    public sealed class GenHealthMigrationScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<Health>();
            b.RegisterMigration<GenHealthV1ToHealth>();
            b.WorldId("GenMigrationWorld");
        }
    }

    public unsafe class MigrationGeneratorTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
            _world = new World("GenMigrationWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
        }

        [Test]
        public void GeneratedStep_PaddedStruct_MigratesCorrectly()
        {
            var step = new global::ECSave.Generated.Migrations.GenPadMigrationStep();

            var old = new GenPadV1 { A = 7, B = 0x1122334455667788L };
            int oldSize = UnsafeUtility.SizeOf<GenPadV1>();
            int newSize = UnsafeUtility.SizeOf<GenPadV2>();

            var src = new byte[oldSize];
            var dst = new byte[newSize];

            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                UnsafeUtility.MemCpy(s, &old, oldSize);

                var reader = new ByteReader(new ByteView(s, oldSize));
                var writer = new ByteWriter(d, newSize);
                step.Migrate(ref reader, ref writer);

                Assert.AreEqual(oldSize, reader.Offset);
                Assert.AreEqual(newSize, writer.Offset);
            }

            GenPadV2 result = default;
            fixed (byte* d = dst)
                UnsafeUtility.MemCpy(&result, d, newSize);

            Assert.AreEqual(7, result.A);
            Assert.AreEqual(0x1122334455667788L, result.B);
            Assert.AreEqual(8, result.C);
        }

        [Test]
        public void GeneratedStep_WrongRecordSize_Throws()
        {
            var step = new global::ECSave.Generated.Migrations.GenPadMigrationStep();
            int newSize = UnsafeUtility.SizeOf<GenPadV2>();
            var src = new byte[UnsafeUtility.SizeOf<GenPadV1>() + 4];
            var dst = new byte[newSize];

            bool threw = false;
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                var reader = new ByteReader(new ByteView(s, src.Length));
                var writer = new ByteWriter(d, newSize);
                try
                {
                    step.Migrate(ref reader, ref writer);
                }
                catch (System.InvalidOperationException)
                {
                    threw = true;
                }
            }

            Assert.IsTrue(threw);
        }

        [Test]
        public void GeneratedStep_Properties_MatchTypes()
        {
            var step = new global::ECSave.Generated.Migrations.GenPadMigrationStep();

            Assert.AreEqual(typeof(GenPadV2).FullName, step.NewTypeName);
            Assert.AreEqual(SchemaHashCore.HashUtf8(typeof(GenPadV2).FullName), step.NewTypeNameHash);
            Assert.AreEqual(new global::ECSave.Generated.Modules.GenPadV2Module().SchemaHash, step.NewSchemaHash);
            Assert.AreNotEqual(step.OldSchemaHash, step.NewSchemaHash);
        }

        [Test]
        public void Scope_RegistersGeneratedStep_WithTypeName()
        {
            var scope = ScriptableObject.CreateInstance<GenPadMigrationScope>();
            try
            {
                var table = scope.GetSaveSystem().Definition.Migrations;
                Assert.IsNotNull(table);
                Assert.AreEqual(1, table.Count);

                int seen = 0;
                foreach (var entry in table.EnumerateEntries())
                {
                    seen++;
                    Assert.AreEqual(typeof(GenPadV2).FullName, entry.NewTypeName);
                }
                Assert.AreEqual(1, seen);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }
        }

        [UnityTest]
        public IEnumerator EndToEnd_V1Save_LoadsThroughGeneratedMigration()
        {
            var oldSystem = new SaveSystem(new SaveDefinition(
                new SaveTypeModule[] { new HealthV1Module() },
                gameDataVersion: 1,
                worldId: "GenMigrationWorld"));

            var em = _world.EntityManager;
            var e = em.CreateEntity();
            em.AddComponentData(e, new HealthV1 { Value = 42 });

            var save = oldSystem.Serialize(_world).GetAwaiter().GetResult();
            Assert.IsTrue(save.IsSuccess, $"Serialize: {save.ErrorCode}");

            _world.Dispose();
            _world = new World("GenMigrationWorld");

            var scope = ScriptableObject.CreateInstance<GenHealthMigrationScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var load = sys.Deserialize(_world, save.Data,
                    RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                    .GetAwaiter().GetResult();
                Assert.IsTrue(load.IsSuccess, $"Deserialize: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
                using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
                Assert.AreEqual(1, vals.Length);
                Assert.AreEqual(42, vals[0].Current);
                Assert.AreEqual(84, vals[0].Max);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }

            yield return null;
        }
    }
}
