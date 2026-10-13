using System.Collections.Generic;
using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class BindingEmitterTests
    {
        [Test]
        public void Emit_ContainsClassName()
        {
            var scope = MakeScope("Game.GameSaveScope",
                new[]
                {
                    MakeRegistration(RegistrationKind.Component, "Game.Domain.Health", 0, ModuleClass.Plain),
                });

            var text = BindingEmitter.Emit(scope);

            Assert.IsTrue(text.Contains("class GameSaveScope_ECSaveBindings"));
            Assert.IsTrue(text.Contains("ISaveModuleBinder"));
        }

        [Test]
        public void Emit_ContainsScopeId()
        {
            var scope = MakeScope("Game.GameSaveScope", new RegistrationRecord[0]);
            var text = BindingEmitter.Emit(scope);

            Assert.IsTrue(text.Contains("ScopeId = \"Game.GameSaveScope\""));
        }

        [Test]
        public void Emit_EmitsComponentModule()
        {
            var scope = MakeScope("S",
                new[]
                {
                    MakeRegistration(RegistrationKind.Component, "Game.Domain.Health", 0, ModuleClass.Plain),
                });

            var text = BindingEmitter.Emit(scope);

            Assert.IsTrue(text.Contains("HealthModule"));
            Assert.IsTrue(text.Contains("RegisterModule"));
        }

        [Test]
        public void Emit_EmitsIdentityModule()
        {
            var scope = MakeScope("S",
                new[]
                {
                    MakeRegistration(RegistrationKind.Identity, "Game.Domain.PlayerId", 0, ModuleClass.Identity),
                });

            var text = BindingEmitter.Emit(scope);

            Assert.IsTrue(text.Contains("PlayerIdIdentityModule"));
            Assert.IsTrue(text.Contains("RegisterIdentityModule"));
        }

        [Test]
        public void Emit_EmptyRegistrations_NoRegistrationLines()
        {
            var scope = MakeScope("S", new RegistrationRecord[0]);
            var text = BindingEmitter.Emit(scope);

            Assert.IsFalse(text.Contains("RegisterModule("));
            Assert.IsFalse(text.Contains("RegisterIdentityModule("));
        }

        private static ScopeDescriptor MakeScope(string fullName, IReadOnlyList<RegistrationRecord> regs)
        {
            return new ScopeDescriptor(
                fullName,
                "Test",
                "S",
                false,
                true,
                regs);
        }

        private static RegistrationRecord MakeRegistration(
            RegistrationKind kind,
            string typeFullName,
            int order,
            ModuleClass classifiedAs)
        {
            return new RegistrationRecord(
                kind,
                string.Empty,
                typeFullName,
                order,
                classifiedAs,
                null,
                System.Array.Empty<byte>(),
                0);
        }
    }
}
