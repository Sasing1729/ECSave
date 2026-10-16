using System;
using NUnit.Framework;
using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Game.MinimalE2E;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests.Runtime
{
    internal sealed class OldNameHealthModule : SaveTypeModulePlain<Health>
    {
        private readonly HealthModule _inner = new HealthModule();

        public override ulong SchemaHash => _inner.SchemaHash;
        public override string FullTypeName => RenamedFromTests.OldName;
        public override byte[] SchemaHashInput => _inner.SchemaHashInput;
        public override string[] FieldNames => _inner.FieldNames;

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
            => throw new NotSupportedException();
    }

    internal sealed class OldNameHealthV1Module : SaveTypeModulePlain<HealthV1>
    {
        public override ulong SchemaHash => HealthV1Module.Hash;
        public override string FullTypeName => RenamedFromTests.OldName;
        public override byte[] SchemaHashInput => HealthV1Module.Input;
        public override string[] FieldNames => HealthV1Module.Names;

        protected override unsafe void WriteValue(Entity entity, EntityManager em, byte* value)
            => throw new NotSupportedException();
    }

    public sealed class RenamedHealthScope : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b)
        {
            b.RegisterComponent<Health>().RenamedFrom("Game.Domain.OldHealth");
            b.WorldId("RenameWorld");
        }
    }

    public class RenamedFromTests
    {
        public const string OldName = "Game.Domain.OldHealth";
        private const string NewName = "Game.Domain.Health";
        private const string WorldName = "RenameWorld";

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

        private static ulong H(string s) => SchemaHashCore.HashUtf8(s);

        private void Restart()
        {
            _world.Dispose();
            _world = new World(WorldName);
        }

        private byte[] SaveWith(SaveTypeModule[] modules, Action<EntityManager> populate)
        {
            var sys = new SaveSystem(new SaveDefinition(modules, 1, WorldName));
            populate(_world.EntityManager);
            var r = sys.SerializeAsync(_world).GetAwaiter().GetResult();
            Assert.IsTrue(r.IsSuccess, $"Serialize failed: {r.ErrorCode}");
            return r.Data;
        }

        private static SaveSystem NewSystem(Action<MigrationTable> configure, params SaveTypeModule[] modules)
        {
            var def = new SaveDefinition(modules, 1, WorldName);
            var table = new MigrationTable();
            configure?.Invoke(table);
            def.Migrations = table;
            return new SaveSystem(def);
        }

        private LoadResult Load(SaveSystem sys, byte[] data)
            => sys.DeserializeAsync(_world, data,
                RestorationScope.EntireWorld, DeletionPolicy.ByRegistration)
                .GetAwaiter().GetResult();

        private static void AddHealth(EntityManager em, int current, int max)
        {
            var e = em.CreateEntity();
            em.AddComponentData(e, new Health { Current = current, Max = max });
        }

        [Test]
        public void RenamedSection_LoadsIntoCurrentModule()
        {
            var data = SaveWith(new SaveTypeModule[] { new OldNameHealthModule() },
                em => AddHealth(em, 42, 100));
            Restart();

            var sys = NewSystem(t => t.RegisterRenamed(H(OldName), H(NewName)), new HealthModule());
            var load = Load(sys, data);

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");
            Assert.AreEqual(1, load.EntityCount);

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(42, vals[0].Current);
            Assert.AreEqual(100, vals[0].Max);
        }

        [Test]
        public void RenamedSection_WithoutMapping_IsIgnoredWithWarning()
        {
            var data = SaveWith(new SaveTypeModule[] { new OldNameHealthModule() },
                em => AddHealth(em, 42, 100));
            Restart();

            var sys = NewSystem(null, new HealthModule());
            var load = Load(sys, data);

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            bool warned = false;
            for (int i = 0; i < load.Warnings.Count; i++)
                if (load.Warnings[i].Kind == LoadWarningKind.UnknownSectionIgnored) warned = true;
            Assert.IsTrue(warned);

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            Assert.AreEqual(0, q.CalculateEntityCount());
        }

        [Test]
        public void RenamedSection_WithSchemaChange_MigratesThroughNewName()
        {
            var data = SaveWith(new SaveTypeModule[] { new OldNameHealthV1Module() }, em =>
            {
                var e = em.CreateEntity();
                em.AddComponentData(e, new HealthV1 { Value = 42 });
            });
            Restart();

            var sys = NewSystem(t =>
            {
                t.RegisterRenamed(H(OldName), H(NewName));
                t.Register(new HealthV1ToV2Migration());
            }, new HealthModule());
            var load = Load(sys, data);

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(42, vals[0].Current);
            Assert.AreEqual(0, vals[0].Max);
        }

        [Test]
        public void OldAndNewSectionsBothPresent_ReturnsSchemaMismatch()
        {
            var data = SaveWith(
                new SaveTypeModule[] { new OldNameHealthModule(), new HealthModule() },
                em => AddHealth(em, 1, 2));
            Restart();

            var sys = NewSystem(t => t.RegisterRenamed(H(OldName), H(NewName)), new HealthModule());
            var load = Load(sys, data);

            Assert.IsFalse(load.IsSuccess);
            Assert.AreEqual(SaveLoadErrorCode.SchemaMismatch, load.ErrorCode);
        }

        [Test]
        public void CurrentName_TakesPriorityOverRename()
        {
            var data = SaveWith(new SaveTypeModule[] { new HealthModule() },
                em => AddHealth(em, 7, 8));
            Restart();

            var sys = NewSystem(t => t.RegisterRenamed(H(NewName), H(OldName)), new HealthModule());
            var load = Load(sys, data);

            Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

            var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
            using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
            Assert.AreEqual(1, vals.Length);
            Assert.AreEqual(7, vals[0].Current);
        }

        [Test]
        public void Scope_RenamedFrom_IsRegisteredAndLoadsOldName()
        {
            var scope = ScriptableObject.CreateInstance<RenamedHealthScope>();
            try
            {
                var sys = scope.GetSaveSystem();
                var table = sys.Definition.Migrations;

                Assert.IsNotNull(table);
                Assert.AreEqual(1, table.RenamedCount);
                Assert.IsTrue(table.TryResolveRenamed(H(OldName), out var target));
                Assert.AreEqual(H(NewName), target);

                var data = SaveWith(new SaveTypeModule[] { new OldNameHealthModule() },
                    em => AddHealth(em, 42, 100));
                Restart();

                var load = Load(sys, data);
                Assert.IsTrue(load.IsSuccess, $"Deserialize failed: {load.ErrorCode}");

                var q = _world.EntityManager.CreateEntityQuery(typeof(Health));
                using var vals = q.ToComponentDataArray<Health>(Allocator.Temp);
                Assert.AreEqual(1, vals.Length);
                Assert.AreEqual(42, vals[0].Current);
            }
            finally
            {
                ScriptableObject.DestroyImmediate(scope);
            }
        }

        [Test]
        public void Builder_RenamedFromSameAsCurrentName_Throws()
        {
            var impl = new SaveDefinitionBuilderImpl();
            var ex = Assert.Throws<SaveRegistrationException>(
                () => impl.RegisterComponent<Health>().RenamedFrom(NewName));
            Assert.AreEqual("ECSAVE-R006", ex.Code);
        }

        [Test]
        public void Builder_SameOldNameForDifferentTypes_Throws()
        {
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Old.Shared");
            var ex = Assert.Throws<SaveRegistrationException>(
                () => impl.RegisterComponent<HealthV1>().RenamedFrom("Old.Shared"));
            Assert.AreEqual("ECSAVE-R007", ex.Code);
        }

        [Test]
        public void Builder_SamePairTwice_IsIdempotent()
        {
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Old.A").RenamedFrom("Old.A");

            var def = impl.Build();
            Assert.IsNotNull(def.Migrations);
            Assert.AreEqual(1, def.Migrations.RenamedCount);
        }

        [Test]
        public void Builder_ChainedRenames_AllRegistered()
        {
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterComponent<Health>().RenamedFrom("Old.A").RenamedFrom("Old.B");

            var def = impl.Build();
            Assert.AreEqual(2, def.Migrations.RenamedCount);
            Assert.IsTrue(def.Migrations.TryResolveRenamed(H("Old.A"), out var a));
            Assert.IsTrue(def.Migrations.TryResolveRenamed(H("Old.B"), out var b));
            Assert.AreEqual(H(NewName), a);
            Assert.AreEqual(H(NewName), b);
        }
    }
}
