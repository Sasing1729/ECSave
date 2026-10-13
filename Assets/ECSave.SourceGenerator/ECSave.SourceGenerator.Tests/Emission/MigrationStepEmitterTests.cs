using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class MigrationStepEmitterTests
    {
        [Test]
        public void Emit_ContainsClassName()
        {
            var text = MigrationStepEmitter.Emit(
                "MyMigration",
                "OldHealth", "NewHealth",
                null, null,
                0xAAAAUL, 0xBBBBUL, 0xCCCCUL);

            Assert.IsTrue(text.Contains("MyMigrationStep"));
            Assert.IsTrue(text.Contains("ECSave.Internal.MigrationStepBase"));
        }

        [Test]
        public void Emit_ContainsHashes()
        {
            var text = MigrationStepEmitter.Emit(
                "M", "O", "N", null, null,
                0x1111111111111111UL,
                0x2222222222222222UL,
                0x3333333333333333UL);

            Assert.IsTrue(text.Contains("0x1111111111111111UL"));
            Assert.IsTrue(text.Contains("0x2222222222222222UL"));
            Assert.IsTrue(text.Contains("0x3333333333333333UL"));
        }

        [Test]
        public void Emit_ContainsImplInstance()
        {
            var text = MigrationStepEmitter.Emit(
                "MyMig", "O", "N", null, null, 0, 0, 0);

            Assert.IsTrue(text.Contains("new global::MyMig()"));
            Assert.IsTrue(text.Contains("_impl.Migrate(in old)"));
        }
    }
}
