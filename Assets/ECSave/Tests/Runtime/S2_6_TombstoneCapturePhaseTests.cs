using System;
using System.Collections.Generic;
using NUnit.Framework;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Serialize;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Tests.Runtime.SubScene
{
    public struct GenCaptureTombstoneId : IComponentData
    {
        public int Value;
    }

    public struct GenCaptureTombstoneOtherId : IComponentData
    {
        public long Value;
    }

    [TestFixture]
    public sealed class S2_6_TombstoneCapturePhaseTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            SubSceneTombstone.ClearForTests();
            _world = new World("S26TestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated) _world.Dispose();
            SubSceneTombstone.ClearForTests();
        }

        private static SubSceneDefinition MakeDef(
            bool register,
            IReadOnlyList<SubSceneDestroyedIdentityHandler> handlers)
        {
            return new SubSceneDefinition(
                false, register, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                null,
                handlers,
                Array.Empty<PrefabMigrationStepBase>());
        }

        [Test]
        public void NullDef_NoSection()
        {
            var r = S2_6_TombstoneCapturePhase.Execute(null, _world);
            Assert.That(r.HasSection, Is.False);
            Assert.That(r.Captured.Count, Is.EqualTo(0));
        }

        [Test]
        public void DisabledRegister_NoSection()
        {
            SubSceneTombstone.Register<GenCaptureTombstoneId>();
            var handlers = SubSceneTombstone.GetRegisteredHandlers();

            var def = MakeDef(register: false, handlers);
            var r = S2_6_TombstoneCapturePhase.Execute(def, _world);
            Assert.That(r.HasSection, Is.False);
        }

        [Test]
        public void NoHandlers_NoSection()
        {
            var def = MakeDef(register: true, Array.Empty<SubSceneDestroyedIdentityHandler>());
            var r = S2_6_TombstoneCapturePhase.Execute(def, _world);
            Assert.That(r.HasSection, Is.False);
        }

        [Test]
        public void EmptySnapshot_NoSection()
        {
            SubSceneTombstone.Register<GenCaptureTombstoneId>();
            var handlers = SubSceneTombstone.GetRegisteredHandlers();

            var def = MakeDef(register: true, handlers);
            var r = S2_6_TombstoneCapturePhase.Execute(def, _world);
            Assert.That(r.HasSection, Is.False,
                "No marks were recorded, so no section should be captured");
        }

        [Test]
        public void WithMarks_SingleHandler_Captured()
        {
            var em = _world.EntityManager;
            SubSceneTombstone.MarkDestroyed(em, new GenCaptureTombstoneId { Value = 42 });
            SubSceneTombstone.MarkDestroyed(em, new GenCaptureTombstoneId { Value = 7 });

            var handlers = SubSceneTombstone.GetRegisteredHandlers();
            var def = MakeDef(register: true, handlers);

            var r = S2_6_TombstoneCapturePhase.Execute(def, _world);

            Assert.That(r.HasSection, Is.True);
            Assert.That(r.Captured.Count, Is.EqualTo(1));

            var cap = r.Captured[0];
            Assert.That(cap.Handler.IdentityType, Is.EqualTo(typeof(GenCaptureTombstoneId)));
            Assert.That(cap.Keys.Count, Is.EqualTo(2));
            Assert.That(cap.SectionBodySize,
                Is.EqualTo(SubSceneTombstoneLayout.ComputeBodySize(2, cap.Handler.KeyStride)));
            Assert.That(cap.SectionName, Does.Contain("GenCaptureTombstoneId"));
        }

        [Test]
        public void WithMarks_MultipleHandlers_EachCaptured()
        {
            var em = _world.EntityManager;
            SubSceneTombstone.MarkDestroyed(em, new GenCaptureTombstoneId { Value = 42 });
            SubSceneTombstone.MarkDestroyed(em, new GenCaptureTombstoneOtherId { Value = 99L });

            var handlers = SubSceneTombstone.GetRegisteredHandlers();
            var def = MakeDef(register: true, handlers);

            var r = S2_6_TombstoneCapturePhase.Execute(def, _world);

            Assert.That(r.HasSection, Is.True);
            Assert.That(r.Captured.Count, Is.EqualTo(2));

            var idTypes = new HashSet<Type>();
            for (int i = 0; i < r.Captured.Count; i++)
                idTypes.Add(r.Captured[i].Handler.IdentityType);

            Assert.That(idTypes.Contains(typeof(GenCaptureTombstoneId)), Is.True);
            Assert.That(idTypes.Contains(typeof(GenCaptureTombstoneOtherId)), Is.True);
        }

        [Test]
        public void EmptyHandlerSkipped_OnlyNonEmptyCaptured()
        {
            var em = _world.EntityManager;
            SubSceneTombstone.MarkDestroyed(em, new GenCaptureTombstoneId { Value = 42 });

            SubSceneTombstone.Register<GenCaptureTombstoneOtherId>();
            var handlers = SubSceneTombstone.GetRegisteredHandlers();

            var def = MakeDef(register: true, handlers);
            var r = S2_6_TombstoneCapturePhase.Execute(def, _world);

            Assert.That(r.Captured.Count, Is.EqualTo(1),
                "Handler with no marks must be skipped from capture");
            Assert.That(r.Captured[0].Handler.IdentityType,
                Is.EqualTo(typeof(GenCaptureTombstoneId)));
        }
    }
}
