using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class IdentityModuleEmitterTests
    {
        [Test]
        public void Emit_ContainsClassName()
        {
            var plan = MakePlan("Game.Domain.PlayerId",
                new[] { new FieldPlan("Value", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4);

            var text = IdentityModuleEmitter.Emit(
                "Game.Domain.PlayerId",
                plan,
                new byte[] { 4, 0 },
                0xABCDEF1234567890UL);

            Assert.IsTrue(text.Contains("class PlayerIdIdentityModule"));
            Assert.IsTrue(text.Contains("ECSave.Internal.IdentityModule"));
        }

        [Test]
        public void Emit_ContainsKeyStride()
        {
            var plan = MakePlan("A",
                new[] { new FieldPlan("Value", FieldKind.Primitive, SchemaHashKind.Int64, 8, 0) },
                structSize: 8);

            var text = IdentityModuleEmitter.Emit(
                "A", plan, new byte[] { 8, 0 }, 0UL);

            Assert.IsTrue(text.Contains("KeyStride => 8"));
        }

        [Test]
        public void Emit_ContainsSchemaHash()
        {
            var plan = MakePlan("A",
                new[] { new FieldPlan("Value", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4);

            var text = IdentityModuleEmitter.Emit(
                "A", plan, new byte[] { 4, 0 }, 0xDEADBEEFCAFEBABEUL);

            Assert.IsTrue(text.Contains("0xDEADBEEFCAFEBABEUL"));
        }

        [Test]
        public void Emit_ContainsBuildWorldIndex()
        {
            var plan = MakePlan("A",
                new[] { new FieldPlan("Value", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4);

            var text = IdentityModuleEmitter.Emit(
                "A", plan, new byte[] { 4, 0 }, 0UL);

            Assert.IsTrue(text.Contains("BuildWorldIndex"));
            Assert.IsTrue(text.Contains("HasWorldConflict"));
            Assert.IsTrue(text.Contains("AddWorldEntry"));
        }

        [Test]
        public void Emit_ContainsBuildSaveIndex()
        {
            var plan = MakePlan("A",
                new[] { new FieldPlan("Value", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4);

            var text = IdentityModuleEmitter.Emit(
                "A", plan, new byte[] { 4, 0 }, 0UL);

            Assert.IsTrue(text.Contains("BuildSaveIndex"));
            Assert.IsTrue(text.Contains("HasSaveConflict"));
            Assert.IsTrue(text.Contains("AddSaveEntry"));
        }

        [Test]
        public void Emit_ContainsApplySectionBody()
        {
            var plan = MakePlan("A",
                new[] { new FieldPlan("Value", FieldKind.Primitive, SchemaHashKind.Int32, 4, 0) },
                structSize: 4);

            var text = IdentityModuleEmitter.Emit(
                "A", plan, new byte[] { 4, 0 }, 0UL);

            Assert.IsTrue(text.Contains("ApplySectionBody"));
            Assert.IsTrue(text.Contains("AddComponentData(target, value)"));
        }

        private static TypePlan MakePlan(string fullName, FieldPlan[] fields, int structSize)
        {
            return new TypePlan(fullName, structSize, fields, new int[0], structSize);
        }
    }
}
