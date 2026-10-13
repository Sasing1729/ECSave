using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class SelectionEmissionTests
    {
        [Test]
        public void Generator_EmitsSelectionModule()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Ref : IComponentData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReference<Ref>(r => r.Target);
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "Ref_TargetSelectionModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("Ref_TargetSelectionModule"));
            Assert.IsTrue(text.Contains("values[i].Target"));
        }

        [Test]
        public void Generator_EmitsBufferSelectionModule()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct RefElem : IBufferElementData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReferences<RefElem>(e => e.Target);
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "RefElem_TargetSelectionModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("GetBufferAccessor"));
        }

        [Test]
        public void Generator_EmitsBindingWithSelection()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Ref : IComponentData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.SelectEntityReference<Ref>(r => r.Target);
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "S_ECSaveBindings.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("RegisterSelectionModule"));
            Assert.IsTrue(text.Contains("Ref_TargetSelectionModule"));
        }

        [Test]
        public void Generator_NoSelectionModule_WhenFieldPathEmpty()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Ref : IComponentData { public Entity Target; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b)
    {
        b.RegisterComponent<Ref>();
    }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "Ref_TargetSelectionModule.g.cs");
            Assert.IsNull(text);
        }
    }
}
