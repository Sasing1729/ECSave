using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class ModuleEmitterTests
    {
        [Test]
        public void Emit_ContainsClassName()
        {
            var plan = MakePlan("Game.Domain.Health",
                new[]
                {
                    new FieldPlan("Current", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0),
                    new FieldPlan("Max", FieldKind.Primitive, SchemaHashKind.Int32, 4, 4),
                },
                structSize: 8, recordStride: 8, refOffsets: new int[0]);

            var input = new byte[] { 8, 0, 4, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            var text = ModuleEmitter.Emit(
                "Game.Domain.Health",
                "SaveTypeModulePlain",
                plan,
                input,
                schemaHash: 0x1234567890ABCDEFUL,
                FilterInfo.Empty,
                false);

            Assert.IsTrue(text.Contains("class HealthModule"));
            Assert.IsTrue(text.Contains("SaveTypeModulePlain<global::Game.Domain.Health>"));
        }

        [Test]
        public void Emit_ContainsSchemaHash()
        {
            var plan = MakePlan("A",
                new[] { new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4, recordStride: 4, refOffsets: new int[0]);

            var text = ModuleEmitter.Emit(
                "A",
                "SaveTypeModulePlain",
                plan,
                new byte[] { 4, 0 },
                schemaHash: 0xDEADBEEFCAFEBABEUL,
                FilterInfo.Empty,
                false);

            Assert.IsTrue(text.Contains("0xDEADBEEFCAFEBABEUL"));
        }

        [Test]
        public void Emit_ContainsFieldNames()
        {
            var plan = MakePlan("A",
                new[]
                {
                    new FieldPlan("Current", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0),
                    new FieldPlan("Max", FieldKind.Primitive, SchemaHashKind.Int32, 4, 4),
                },
                structSize: 8, recordStride: 8, refOffsets: new int[0]);

            var text = ModuleEmitter.Emit(
                "A", "SaveTypeModulePlain", plan, new byte[] { 8, 0 }, 0UL,
                FilterInfo.Empty, false);

            Assert.IsTrue(text.Contains("\"Current\""));
            Assert.IsTrue(text.Contains("\"Max\""));
        }

        [Test]
        public void Emit_ZeroSize_UsesAddOnly()
        {
            var plan = MakePlan("Game.Domain.TagComponent",
                new FieldPlan[0],
                structSize: 0, recordStride: 0, refOffsets: new int[0]);

            var text = ModuleEmitter.Emit(
                "Game.Domain.TagComponent",
                "SaveTypeModulePlain",
                plan,
                new byte[] { 0, 0 },
                0UL,
                FilterInfo.Empty,
                false);

            Assert.IsTrue(text.Contains("AddComponentData(entity, default(global::Game.Domain.TagComponent))"));
            Assert.IsFalse(text.Contains("MemCpy"));
        }

        [Test]
        public void Emit_NonZeroSize_UsesMemCpy()
        {
            var plan = MakePlan("A",
                new[] { new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4, recordStride: 4, refOffsets: new int[0]);

            var text = ModuleEmitter.Emit(
                "A", "SaveTypeModulePlain", plan, new byte[] { 4, 0 }, 0UL,
                FilterInfo.Empty, false);

            Assert.IsTrue(text.Contains("UnsafeUtility.MemCpy"));
            Assert.IsTrue(text.Contains("UnsafeUtility.AddressOf(ref v)"));
        }

        [Test]
        public void Emit_NamespaceIsGeneratedModules()
        {
            var plan = MakePlan("A",
                new[] { new FieldPlan("X", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4, recordStride: 4, refOffsets: new int[0]);

            var text = ModuleEmitter.Emit(
                "A", "SaveTypeModulePlain", plan, new byte[] { 4, 0 }, 0UL,
                FilterInfo.Empty, false);

            Assert.IsTrue(text.Contains("namespace ECSave.Generated.Modules"));
        }

        [Test]
        public void Emit_Enableable_BaseClassName()
        {
            var plan = MakePlan("Game.Domain.Stunned",
                new[] { new FieldPlan("Frames", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4, recordStride: 4, refOffsets: new int[0]);

            var text = ModuleEmitter.Emit(
                "Game.Domain.Stunned",
                "SaveTypeModuleEnableable",
                plan,
                new byte[] { 4, 0 },
                0UL,
                FilterInfo.Empty,
                false);

            Assert.IsTrue(text.Contains("SaveTypeModuleEnableable<global::Game.Domain.Stunned>"));
        }

        private static TypePlan MakePlan(
            string fullName,
            FieldPlan[] fields,
            int structSize,
            int recordStride,
            int[] refOffsets)
        {
            return new TypePlan(fullName, structSize, fields, refOffsets, recordStride);
        }
    }
}
