using ECSave.SourceGenerator.Emission;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class SelfCheckEmitterTests
    {
        [Test]
        public void Emit_ContainsClassName()
        {
            var text = SelfCheckEmitter.Emit("Game.Domain.Health", 8);
            Assert.IsTrue(text.Contains("Health_ECSaveSelfCheck"));
        }

        [Test]
        public void Emit_ContainsExpectedSize()
        {
            var text = SelfCheckEmitter.Emit("A", 16);
            Assert.IsTrue(text.Contains("ExpectedSize = 16"));
        }

        [Test]
        public void Emit_ContainsActualSizeCall()
        {
            var text = SelfCheckEmitter.Emit("A", 4);
            Assert.IsTrue(text.Contains("UnsafeUtility.SizeOf<global::A>()"));
        }

        [Test]
        public void Emit_ContainsVerifyMethod()
        {
            var text = SelfCheckEmitter.Emit("A", 4);
            Assert.IsTrue(text.Contains("public static bool Verify()"));
            Assert.IsTrue(text.Contains("ActualSize() == ExpectedSize"));
        }

        [Test]
        public void Emit_Namespace()
        {
            var text = SelfCheckEmitter.Emit("A", 4);
            Assert.IsTrue(text.Contains("namespace ECSave.Generated.SelfCheck"));
        }
    }
}
