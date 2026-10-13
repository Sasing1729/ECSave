using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class BindingEmitterExtendedTests
    {
        [Test]
        public void MultipleComponents_AllEmittedInOrder()
        {
            var scope = MakeScope("S", new[]
            {
                MakeReg(RegistrationKind.Component, "A", 0, ModuleClass.Plain),
                MakeReg(RegistrationKind.Component, "B", 1, ModuleClass.Plain),
                MakeReg(RegistrationKind.Component, "C", 2, ModuleClass.Plain),
            });
            var text = BindingEmitter.Emit(scope);

            int iA = text.IndexOf("AModule");
            int iB = text.IndexOf("BModule");
            int iC = text.IndexOf("CModule");
            Assert.Less(iA, iB);
            Assert.Less(iB, iC);
        }

        [Test]
        public void BufferModule_EmitsViaRegisterModule()
        {
            var scope = MakeScope("S", new[]
            {
                MakeReg(RegistrationKind.Buffer, "Item", 0, ModuleClass.Buffer),
            });
            var text = BindingEmitter.Emit(scope);
            Assert.IsTrue(text.Contains("ItemModule"));
            Assert.IsTrue(text.Contains("RegisterModule"));
        }

        [Test]
        public void SharedModule_EmitsViaRegisterModule()
        {
            var scope = MakeScope("S", new[]
            {
                MakeReg(RegistrationKind.SharedComponent, "Faction", 0, ModuleClass.Shared),
            });
            var text = BindingEmitter.Emit(scope);
            Assert.IsTrue(text.Contains("FactionModule"));
            Assert.IsTrue(text.Contains("RegisterModule"));
        }

        [Test]
        public void ChunkModule_EmitsViaRegisterModule()
        {
            var scope = MakeScope("S", new[]
            {
                MakeReg(RegistrationKind.ChunkComponent, "ChunkData", 0, ModuleClass.Chunk),
            });
            var text = BindingEmitter.Emit(scope);
            Assert.IsTrue(text.Contains("ChunkDataModule"));
            Assert.IsTrue(text.Contains("RegisterModule"));
        }

        [Test]
        public void MixedModules_CorrectRegisterCalls()
        {
            var scope = MakeScope("S", new[]
            {
                MakeReg(RegistrationKind.Component, "Health", 0, ModuleClass.Plain),
                MakeReg(RegistrationKind.Identity, "PlayerId", 1, ModuleClass.Identity),
            });
            var text = BindingEmitter.Emit(scope);
            Assert.IsTrue(text.Contains("RegisterModule(new global::ECSave.Generated.Modules.HealthModule())"));
            Assert.IsTrue(text.Contains("RegisterIdentityModule(new global::ECSave.Generated.Modules.PlayerIdIdentityModule())"));
        }

        private static ScopeDescriptor MakeScope(string fullName, RegistrationRecord[] regs)
            => new ScopeDescriptor(fullName, "Test", "S", false, true, regs);

        private static RegistrationRecord MakeReg(
            RegistrationKind kind, string typeName, int order, ModuleClass cls)
            => new RegistrationRecord(
                kind, string.Empty, typeName, order, cls, null, System.Array.Empty<byte>(), 0);
    }
}
