using ECSave.SourceGenerator.Tests.TestHelpers;
using NUnit.Framework;

namespace ECSave.SourceGenerator.Tests.Emission
{
    public class ModuleShapeTests
    {
        [Test]
        public void PlainModule_ContainsSchemaHash()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int Current; public int Max; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Health>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "HealthModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("s_schemaHash"));
            Assert.IsTrue(text.Contains("s_schemaHashInput"));
            Assert.IsTrue(text.Contains("s_fieldNames"));
        }

        [Test]
        public void PlainModule_EmitsWriteValue()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Health : IComponentData { public int V; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Health>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "HealthModule.g.cs");
            Assert.IsTrue(text.Contains("protected override unsafe void WriteValue"));
            Assert.IsTrue(text.Contains("UnsafeUtility.MemCpy"));
        }

        [Test]
        public void EnableableModule_HasCorrectBase()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Stunned : IComponentData, IEnableableComponent { public int F; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Stunned>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "StunnedModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModuleEnableable<global::Stunned>"));
            Assert.IsTrue(text.Contains("protected override unsafe void WriteValue"));
        }

        [Test]
        public void BufferModule_NoWriteValue()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Item : IBufferElementData { public int Id; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterBuffer<Item>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ItemModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModuleBuffer<global::Item>"));
            Assert.IsFalse(text.Contains("WriteValue"),
                "Buffer module should not emit WriteValue");
        }

        [Test]
        public void BufferEnableableModule_NoWriteValue()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Eff : IBufferElementData, IEnableableComponent { public int Id; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterBuffer<Eff>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "EffModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModuleBufferEnableable<global::Eff>"));
            Assert.IsFalse(text.Contains("WriteValue"));
        }

        [Test]
        public void SharedModule_NoWriteValue()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Faction : ISharedComponentData { public int Id; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterSharedComponent<Faction>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "FactionModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModuleShared<global::Faction>"));
            Assert.IsFalse(text.Contains("WriteValue"));
        }

        [Test]
        public void ChunkModule_NoWriteValue()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct ChunkData : IComponentData { public int V; }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterChunkComponent<ChunkData>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "ChunkDataModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("SaveTypeModuleChunk<global::ChunkData>"));
            Assert.IsFalse(text.Contains("WriteValue"));
        }

        [Test]
        public void ZeroSizePlainModule_AddOnly()
        {
            var src = @"
using ECSave;
using Unity.Entities;

public struct Tag : IComponentData { }

public sealed class S : SaveScope
{
    protected override void Configure(ISaveDefinitionBuilder b) { b.RegisterComponent<Tag>(); }
}";
            var result = GeneratorTestHarness.Run(src);
            var text = GeneratorTestHarness.GetGenerated(result, "TagModule.g.cs");
            Assert.IsNotNull(text);
            Assert.IsTrue(text.Contains("AddComponentData(entity, default(global::Tag))"));
            Assert.IsFalse(text.Contains("UnsafeUtility.MemCpy"));
        }
    }
}
