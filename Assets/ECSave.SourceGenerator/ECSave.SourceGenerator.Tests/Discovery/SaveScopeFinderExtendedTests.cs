using ECSave.SourceGenerator.Discovery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Discovery
{
    public class SaveScopeFinderExtendedTests
    {
        [Test]
        public void GlobalNamespaceScope_Detected()
        {
            var src = @"
using ECSave;

public sealed class RootScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var desc = CreateDescriptor(src);
            Assert.IsNotNull(desc);
            Assert.AreEqual("RootScope", desc.ClassName);
            Assert.AreEqual("", desc.NamespaceName);
            Assert.IsTrue(desc.ConfigureFound);
        }

        [Test]
        public void NestedNamespaceScope_FullNameIncludesNamespace()
        {
            var src = @"
using ECSave;

namespace Game.Deeply.Nested
{
    public sealed class S : SaveScope
    {
        protected override void Configure(ISaveDefinitionBuilder b) { }
    }
}";
            var desc = CreateDescriptor(src);
            Assert.IsNotNull(desc);
            Assert.AreEqual("Game.Deeply.Nested.S", desc.FullTypeName);
            Assert.AreEqual("Game.Deeply.Nested", desc.NamespaceName);
        }

        [Test]
        public void DerivedChainScope_Detected()
        {
            var src = @"
using ECSave;

public abstract class BaseScope : SaveScope { }
public sealed class DerivedScope : BaseScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var desc = CreateDescriptorOfType(src, "DerivedScope");
            Assert.IsNotNull(desc);
        }

        [Test]
        public void ScopeWithoutConfigure_ConfigureFoundFalse()
        {
            var src = @"
using ECSave;

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var desc = CreateDescriptor(src);
            Assert.IsTrue(desc.ConfigureFound);
        }

        [Test]
        public void AbstractScope_IsAbstractTrue()
        {
            var src = @"
using ECSave;

public abstract class AbstractScope : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { }
}";
            var desc = CreateDescriptor(src);
            Assert.IsTrue(desc.IsAbstract);
        }

        private static ScopeDescriptor CreateDescriptor(string src)
        {
            return CreateDescriptorOfType(src, null);
        }

        private static ScopeDescriptor CreateDescriptorOfType(string src, string name)
        {
            var stub = StubSourceBuilder.BuilderStubs();
            var tree = CSharpSyntaxTree.ParseText(src);
            var stubTree = CSharpSyntaxTree.ParseText(stub);
            var compilation = CSharpCompilation.Create(
                "Test",
                new[] { tree, stubTree },
                ReferenceAssemblies.Get(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax cd)
                {
                    if (name != null && cd.Identifier.Text != name) continue;
                    var sym = model.GetDeclaredSymbol(cd) as INamedTypeSymbol;
                    if (sym == null) continue;
                    if (!WellKnownTypes.IsSaveScope(sym)) continue;
                    return SaveScopeFinder.CreateDescriptor(sym, compilation);
                }
            }
            return null;
        }
    }
}
