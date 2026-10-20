using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.Contract;

namespace ECSave.Tests.Generator
{
    [TestFixture]
    public sealed class SubSceneGeneratedArtifactTests
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
        }

        [Test]
        public void Scope_WithConfigureSubScene_ImplementsISubSceneBinder()
        {
            var id = typeof(GenSubSceneScope).FullName;
            Assert.That(GeneratedScopeRegistry.TryGet(id, out var main), Is.True);
            Assert.That(main, Is.InstanceOf<ISubSceneBinder>());
        }

        [Test]
        public void Scope_WithoutConfigureSubScene_DoesNotImplementISubSceneBinder()
        {
            var id = typeof(GenTestScope).FullName;
            Assert.That(GeneratedScopeRegistry.TryGet(id, out var main), Is.True);
            Assert.That(main, Is.Not.InstanceOf<ISubSceneBinder>());
        }

        [Test]
        public void TryGetSubScene_ReturnsTrueForSubSceneScope()
        {
            var id = typeof(GenSubSceneScope).FullName;
            Assert.That(GeneratedScopeRegistry.TryGetSubScene(id, out _), Is.True);
        }

        [Test]
        public void TryGetSubScene_ReturnsFalseForPlainScope()
        {
            var id = typeof(GenTestScope).FullName;
            Assert.That(GeneratedScopeRegistry.TryGetSubScene(id, out _), Is.False);
        }

        [Test]
        public void PrefabMigrationScope_ImplementsISubSceneBinder()
        {
            var id = typeof(GenPrefabMigrationScope).FullName;
            Assert.That(GeneratedScopeRegistry.TryGet(id, out var main), Is.True);
            Assert.That(main, Is.InstanceOf<ISubSceneBinder>());
        }

        [Test]
        public void PrefabMigrationScope_GeneratesStepType()
        {
            var stepType = typeof(GenPrefabMigrationScope)
                .Assembly
                .GetType("ECSave.Generated.Migrations.GenPrefabMigrationStep");
            Assert.That(stepType, Is.Not.Null, "PrefabMigrationStep.g.cs should be generated");
        }
    }
}
