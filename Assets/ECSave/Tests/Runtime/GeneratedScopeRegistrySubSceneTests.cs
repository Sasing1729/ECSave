using System.Collections.Generic;
using NUnit.Framework;
using ECSave;
using ECSave.Internal.Contract;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public sealed class GeneratedScopeRegistrySubSceneTests
    {
        private IReadOnlyDictionary<string, ISaveModuleBinder> _snapshot;

        [SetUp]
        public void SetUp()
        {
            _snapshot = GeneratedScopeRegistry.Snapshot();
        }

        [TearDown]
        public void TearDown()
        {
            GeneratedScopeRegistry.Restore(_snapshot);
        }

        private sealed class PlainBinder : ISaveModuleBinder
        {
            public void BindModules(ISaveDefinitionBuilder builder) { }
        }

        private sealed class ComboBinder : ISaveModuleBinder, ISubSceneBinder
        {
            public void BindModules(ISaveDefinitionBuilder builder) { }
            public void BindSubScene(ISubSceneDefinitionBuilder builder) { }
        }

        [Test]
        public void Register_PlainBinder_TryGetTrue_TryGetSubSceneFalse()
        {
            var id = "Test.Plain_" + System.Guid.NewGuid();
            GeneratedScopeRegistry.Register(id, new PlainBinder());

            Assert.That(GeneratedScopeRegistry.TryGet(id, out _), Is.True);
            Assert.That(GeneratedScopeRegistry.TryGetSubScene(id, out _), Is.False);
        }

        [Test]
        public void Register_ComboBinder_BothTryGetTrue()
        {
            var id = "Test.Combo_" + System.Guid.NewGuid();
            GeneratedScopeRegistry.Register(id, new ComboBinder());

            Assert.That(GeneratedScopeRegistry.TryGet(id, out var main), Is.True);
            Assert.That(main, Is.InstanceOf<ComboBinder>());
            Assert.That(GeneratedScopeRegistry.TryGetSubScene(id, out var sub), Is.True);
            Assert.That(sub, Is.SameAs(main));
        }

        [Test]
        public void TryGetSubScene_UnknownId_ReturnsFalse()
        {
            var id = "Test.Missing_" + System.Guid.NewGuid();
            Assert.That(GeneratedScopeRegistry.TryGetSubScene(id, out _), Is.False);
        }

        [Test]
        public void Register_DuplicateSameType_NoThrow()
        {
            var id = "Test.Same_" + System.Guid.NewGuid();
            GeneratedScopeRegistry.Register(id, new ComboBinder());
            Assert.DoesNotThrow(() => GeneratedScopeRegistry.Register(id, new ComboBinder()));
        }

        [Test]
        public void Register_DuplicateDifferentType_Throws()
        {
            var id = "Test.Diff_" + System.Guid.NewGuid();
            GeneratedScopeRegistry.Register(id, new PlainBinder());
            Assert.Throws<SaveRegistrationException>(
                () => GeneratedScopeRegistry.Register(id, new ComboBinder()));
        }

        [Test]
        public void Remove_RemovesBothMainAndSubScene()
        {
            var id = "Test.Remove_" + System.Guid.NewGuid();
            GeneratedScopeRegistry.Register(id, new ComboBinder());

            GeneratedScopeRegistry.Remove(id);

            Assert.That(GeneratedScopeRegistry.TryGet(id, out _), Is.False);
            Assert.That(GeneratedScopeRegistry.TryGetSubScene(id, out _), Is.False);
        }

        [Test]
        public void Clear_RemovesAll()
        {
            GeneratedScopeRegistry.Clear();
            Assert.That(GeneratedScopeRegistry.TryGet("anything", out _), Is.False);
            Assert.That(GeneratedScopeRegistry.TryGetSubScene("anything", out _), Is.False);
        }

        [Test]
        public void SnapshotRestore_PreservesBoth()
        {
            var id = "Test.Snapshot_" + System.Guid.NewGuid();
            GeneratedScopeRegistry.Register(id, new ComboBinder());

            var snap = GeneratedScopeRegistry.Snapshot();
            GeneratedScopeRegistry.Clear();

            Assert.That(GeneratedScopeRegistry.TryGetSubScene(id, out _), Is.False);

            GeneratedScopeRegistry.Restore(snap);

            Assert.That(GeneratedScopeRegistry.TryGet(id, out _), Is.True);
            Assert.That(GeneratedScopeRegistry.TryGetSubScene(id, out _), Is.True);
        }
    }
}
