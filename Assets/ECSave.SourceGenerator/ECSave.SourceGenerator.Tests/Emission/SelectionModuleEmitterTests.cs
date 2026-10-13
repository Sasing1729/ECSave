using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class SelectionModuleEmitterTests
    {
        [Test]
        public void Emit_ContainsClassName()
        {
            var text = SelectionModuleEmitter.Emit(
                "Game.Domain.Ref", "Target", isBuffer: false);

            Assert.IsTrue(text.Contains("Ref_TargetSelectionModule"));
            Assert.IsTrue(text.Contains("ECSave.Internal.SelectionModule"));
        }

        [Test]
        public void Emit_ContainsTypeAndFieldPath()
        {
            var text = SelectionModuleEmitter.Emit(
                "Game.Domain.Ref", "Target", isBuffer: false);

            Assert.IsTrue(text.Contains("FullTypeName => \"Game.Domain.Ref\""));
            Assert.IsTrue(text.Contains("FieldPath => \"Target\""));
        }

        [Test]
        public void Emit_Component_ExtractsField()
        {
            var text = SelectionModuleEmitter.Emit(
                "Ref", "Target", isBuffer: false);

            Assert.IsTrue(text.Contains("values[i].Target"));
            Assert.IsTrue(text.Contains("selected.Add(target)"));
        }

        [Test]
        public void Emit_Buffer_IteratesElements()
        {
            var text = SelectionModuleEmitter.Emit(
                "RefElem", "Target", isBuffer: true);

            Assert.IsTrue(text.Contains("GetBufferAccessor"));
            Assert.IsTrue(text.Contains("buf[k].Target"));
            Assert.IsTrue(text.Contains("selected.Add(target)"));
        }

        [Test]
        public void Emit_NestedFieldPath()
        {
            var text = SelectionModuleEmitter.Emit(
                "Ref", "Nested.Target", isBuffer: false);

            Assert.IsTrue(text.Contains("values[i].Nested.Target"));
        }

        [Test]
        public void Emit_Namespace()
        {
            var text = SelectionModuleEmitter.Emit(
                "Ref", "Target", isBuffer: false);

            Assert.IsTrue(text.Contains("namespace ECSave.Generated.Selections"));
        }
    }
}
