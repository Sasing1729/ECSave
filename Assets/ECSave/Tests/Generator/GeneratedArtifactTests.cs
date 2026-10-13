using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.Contract;
using Unity.Collections.LowLevel.Unsafe;
using ECSave.Generated;

namespace ECSave.Tests.Generator
{
    public class GeneratedArtifactTests
    {
        [SetUp]
        public void SetUp()
        {
            MainThreadDetector.RecordMainThreadId();
        }

        [Test]
        public void Registry_ContainsGenTestScope()
        {
            var id = typeof(GenTestScope).FullName;
            Assert.IsTrue(GeneratedScopeRegistry.TryGet(id, out _),
                "GenTestScope should be auto-registered by the generator");
        }

        [Test]
        public void Registry_ContainsGenSecondaryScope()
        {
            var id = typeof(GenSecondaryScope).FullName;
            Assert.IsTrue(GeneratedScopeRegistry.TryGet(id, out _),
                "GenSecondaryScope should be auto-registered");
        }

        [Test]
        public void Registry_ContainsScopesInScopeNamesArray()
        {
            var names = ECSaveScopeRegistry.ScopeNames;
            Assert.Contains(typeof(GenTestScope).FullName, names);
            Assert.Contains(typeof(GenSecondaryScope).FullName, names);
        }

        [Test]
        public void Registry_RegistrationCountsArePositive()
        {
            var counts = ECSaveScopeRegistry.RegistrationCounts;
            Assert.AreEqual(ECSaveScopeRegistry.ScopeNames.Length, counts.Length);

            for (int i = 0; i < counts.Length; i++)
                Assert.GreaterOrEqual(counts[i], 0);
        }

        [Test]
        public void Module_GenHealthModule_Exists()
        {
            var m = new Generated.Modules.GenHealthModule();
            Assert.IsNotNull(m);
            Assert.AreEqual(typeof(GenHealth).FullName, m.FullTypeName);
            Assert.AreNotEqual(0UL, m.SchemaHash);
        }

        [Test]
        public void Module_GenStunnedModule_HasEnabledBits()
        {
            var m = new Generated.Modules.GenStunnedModule();
            Assert.IsTrue(m.HasEnabledBits);
        }

        [Test]
        public void Module_GenInventoryItemModule_IsBuffer()
        {
            var m = new Generated.Modules.GenInventoryItemModule();
            Assert.AreEqual(ECSave.Format.SectionKind.Buffer, m.SectionKind);
        }

        [Test]
        public void Module_GenRefModule_HasEntityRefOffset()
        {
            var m = new Generated.Modules.GenRefModule();
            Assert.AreEqual(1, m.EntityRefOffsets.Length);
            Assert.AreEqual(0, m.EntityRefOffsets[0]);
        }

        [Test]
        public void Module_GenPlayerIdIdentityModule_Exists()
        {
            var m = new Generated.Modules.GenPlayerIdIdentityModule();
            Assert.IsNotNull(m);
            Assert.AreEqual(typeof(GenPlayerId).FullName, m.FullTypeName);
        }

        [Test]
        public void Module_SchemaHash_IsStable()
        {
            var a = new Generated.Modules.GenHealthModule();
            var b = new Generated.Modules.GenHealthModule();
            Assert.AreEqual(a.SchemaHash, b.SchemaHash);
        }

        [Test]
        public void Binding_GenTestScope_HasCorrectScopeId()
        {
            var scopeId = Generated.Bindings.GenTestScope_ECSaveBindings.ScopeId;
            Assert.AreEqual(typeof(GenTestScope).FullName, scopeId);
        }

        [Test]
        public void Binding_GenTestScope_BindModulesAddsAllModules()
        {
            var impl = new SaveDefinitionBuilderImpl();
            var binder = new Generated.Bindings.GenTestScope_ECSaveBindings();
            binder.BindModules(impl);

            var def = impl.Build();
            Assert.GreaterOrEqual(def.Modules.Count, 4);
            Assert.GreaterOrEqual(def.Identities.Count, 1);
        }

        [Test]
        public void SelfCheck_GenHealth_MatchesActualSize()
        {
            int actual = UnsafeUtility.SizeOf<GenHealth>();
            Assert.AreEqual(actual, Generated.SelfCheck.GenHealth_ECSaveSelfCheck.ExpectedSize);
        }

        [Test]
        public void SelfCheck_GenStunned_MatchesActualSize()
        {
            int actual = UnsafeUtility.SizeOf<GenStunned>();
            Assert.AreEqual(actual, Generated.SelfCheck.GenStunned_ECSaveSelfCheck.ExpectedSize);
        }

        [Test]
        public void SelfCheck_GenPlayerId_MatchesActualSize()
        {
            int actual = UnsafeUtility.SizeOf<GenPlayerId>();
            Assert.AreEqual(actual, Generated.SelfCheck.GenPlayerId_ECSaveSelfCheck.ExpectedSize);
        }

        [Test]
        public void SelfCheck_GenRef_MatchesActualSize()
        {
            int actual = UnsafeUtility.SizeOf<GenRef>();
            Assert.AreEqual(actual, Generated.SelfCheck.GenRef_ECSaveSelfCheck.ExpectedSize);
        }
    }
}
