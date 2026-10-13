using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Smoke
{
    public class GeneratorSmokeTests
    {
        [Test]
        public void Generator_EmitsContractVersion_WhenScopeExists()
        {
            var src = @"
using ECSave;

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveContractVersion.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("GeneratorContractVersion"));
            Assert.IsTrue(text.Contains("Value = 1"));
        }

        [Test]
        public void Generator_EmitsScopeRegistry_WhenScopeExists()
        {
            var src = @"
using ECSave;

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("ECSaveScopeRegistry"));
            Assert.IsTrue(text.Contains("ScopeNames"));
            Assert.IsTrue(text.Contains("RegistrationCounts"));
        }

        [Test]
        public void Generator_NoOutput_WithoutSaveScope()
        {
            var result = GeneratorTestHarness.Run("public class Empty { }");
            Assert.IsNull(GeneratorTestHarness.GetGenerated(result, "ECSaveContractVersion.g.cs"));
            Assert.IsNull(GeneratorTestHarness.GetGenerated(result, "ECSaveScopeRegistry.g.cs"));
        }

        [Test]
        public void Generator_NoDiagnosticsOnEmptyInput()
        {
            var result = GeneratorTestHarness.Run("public class Empty { }");
            Assert.AreEqual(0, result.Diagnostics.Length);
        }
    }
}
