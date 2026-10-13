using System.Linq;
using ECSave.SourceGenerator.Discovery;
using ECSave.SourceGenerator.Emission;
using ECSave.SourceGenerator.Tests.TestHelpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class TypeModuleClassifierTests
    {
        [Test]
        public void PlainComponent()
        {
            Assert.AreEqual(ModuleClass.Plain, Classify(
                "public struct Health : Unity.Entities.IComponentData { public int V; }",
                RegistrationKind.Component));
        }

        [Test]
        public void EnableableComponent()
        {
            Assert.AreEqual(ModuleClass.Enableable, Classify(
                "public struct Stunned : Unity.Entities.IComponentData, Unity.Entities.IEnableableComponent { public int F; }",
                RegistrationKind.Component));
        }

        [Test]
        public void Buffer()
        {
            Assert.AreEqual(ModuleClass.Buffer, Classify(
                "public struct Inv : Unity.Entities.IBufferElementData { public int Id; }",
                RegistrationKind.Buffer));
        }

        [Test]
        public void BufferEnableable()
        {
            Assert.AreEqual(ModuleClass.BufferEnableable, Classify(
                "public struct S : Unity.Entities.IBufferElementData, Unity.Entities.IEnableableComponent { public int V; }",
                RegistrationKind.Buffer));
        }

        [Test]
        public void SharedComponent()
        {
            Assert.AreEqual(ModuleClass.Shared, Classify(
                "public struct Faction : Unity.Entities.ISharedComponentData { public int Id; }",
                RegistrationKind.SharedComponent));
        }

        [Test]
        public void ChunkComponent()
        {
            Assert.AreEqual(ModuleClass.Chunk, Classify(
                "public struct ChunkData : Unity.Entities.IComponentData { public int V; }",
                RegistrationKind.ChunkComponent));
        }

        [Test]
        public void IdentityKind()
        {
            Assert.AreEqual(ModuleClass.Identity, Classify(
                "public struct PlayerId : Unity.Entities.IComponentData { public int V; }",
                RegistrationKind.Identity));
        }

        [Test]
        public void ToBaseClassName_MapsAll()
        {
            Assert.AreEqual("SaveTypeModulePlain",
                TypeModuleClassifier.ToBaseClassName(ModuleClass.Plain));
            Assert.AreEqual("SaveTypeModuleEnableable",
                TypeModuleClassifier.ToBaseClassName(ModuleClass.Enableable));
            Assert.AreEqual("SaveTypeModuleShared",
                TypeModuleClassifier.ToBaseClassName(ModuleClass.Shared));
            Assert.AreEqual("SaveTypeModuleBuffer",
                TypeModuleClassifier.ToBaseClassName(ModuleClass.Buffer));
            Assert.AreEqual("SaveTypeModuleBufferEnableable",
                TypeModuleClassifier.ToBaseClassName(ModuleClass.BufferEnableable));
            Assert.AreEqual("SaveTypeModuleChunk",
                TypeModuleClassifier.ToBaseClassName(ModuleClass.Chunk));
            Assert.AreEqual("IdentityModule",
                TypeModuleClassifier.ToBaseClassName(ModuleClass.Identity));
        }

        private static ModuleClass Classify(string source, RegistrationKind kind)
        {
            var stub = StubSourceBuilder.BuilderStubs();
            var tree = CSharpSyntaxTree.ParseText(source);
            var stubTree = CSharpSyntaxTree.ParseText(stub);
            var compilation = CSharpCompilation.Create(
                "Test",
                new[] { tree, stubTree },
                ReferenceAssemblies.Get(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);
            INamedTypeSymbol sym = null;
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is Microsoft.CodeAnalysis.CSharp.Syntax.StructDeclarationSyntax sd)
                {
                    sym = model.GetDeclaredSymbol(sd) as INamedTypeSymbol;
                    if (sym != null) break;
                }
            }

            return TypeModuleClassifier.Classify(sym, kind);
        }
    }
}
