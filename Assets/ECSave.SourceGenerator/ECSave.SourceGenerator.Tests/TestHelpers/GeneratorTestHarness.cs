using System.Linq;
using ECSave.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ECSave.SourceGenerator.Tests.TestHelpers
{
    internal static class GeneratorTestHarness
    {
        public static GeneratorDriverRunResult Run(string source)
        {
            var stub = StubSourceBuilder.BuilderStubs();
            var tree = CSharpSyntaxTree.ParseText(source);
            var stubTree = CSharpSyntaxTree.ParseText(stub);

            var compilation = CSharpCompilation.Create(
                "TestAssembly",
                new[] { tree, stubTree },
                ReferenceAssemblies.Get(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var driver = CSharpGeneratorDriver.Create(new ECSaveGenerator());
            return driver.RunGenerators(compilation).GetRunResult();
        }

        public static string GetGenerated(GeneratorDriverRunResult result, string fileNameSuffix)
        {
            var tree = result.GeneratedTrees.FirstOrDefault(
                t => t.FilePath.EndsWith(fileNameSuffix));
            return tree?.ToString();
        }
    }
}
