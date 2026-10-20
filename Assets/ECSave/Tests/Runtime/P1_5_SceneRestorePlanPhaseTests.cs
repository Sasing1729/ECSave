using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Deserialize.Apply;
using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public unsafe class P1_5_SceneRestorePlanPhaseTests
    {
        private static SubSceneDefinition EnabledDef()
        {
            return new SubSceneDefinition(
                true, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                System.Array.Empty<PrefabMigrationStepBase>());
        }

        private static SubSceneStateSectionView? MakeView(
            NativeArray<byte> buffer, SceneLoadInfo[] scenes)
        {
            byte* ptr = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(buffer);
            var writer = new FormatWriter(ptr, buffer.Length);
            SubSceneStateSection.Write(ref writer, scenes);
            int written = SubSceneStateLayout.ComputeBodySize(scenes.Length);
            return SubSceneStateSection.Read(ptr, written);
        }

        [Test]
        public void NullDef_ReturnsEmpty()
        {
            var err = P1_5_SceneRestorePlanPhase.Execute(null, null, out var requests);
            Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            Assert.That(requests, Is.Empty);
        }

        [Test]
        public void DisabledDef_ReturnsEmpty()
        {
            var err = P1_5_SceneRestorePlanPhase.Execute(
                SubSceneDefinition.Empty, null, out var requests);
            Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            Assert.That(requests, Is.Empty);
        }

        [Test]
        public void NullSavedState_ReturnsEmpty()
        {
            var err = P1_5_SceneRestorePlanPhase.Execute(
                EnabledDef(), null, out var requests);
            Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            Assert.That(requests, Is.Empty);
        }

        [Test]
        public void EmptySavedState_ReturnsEmpty()
        {
            var buffer = new NativeArray<byte>(64, Allocator.TempJob);
            try
            {
                var view = MakeView(buffer, new SceneLoadInfo[0]);
                var err = P1_5_SceneRestorePlanPhase.Execute(
                    EnabledDef(), view, out var requests);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(requests, Is.Empty);
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void SingleScene_OneRequestWithCorrectFields()
        {
            var buffer = new NativeArray<byte>(128, Allocator.TempJob);
            try
            {
                var scenes = new SceneLoadInfo[]
                {
                    new SceneLoadInfo(new SceneGuid(0xAA, 0xBB),
                        SceneLoadState.Loaded, SceneRestoreMode.Optional),
                };
                var view = MakeView(buffer, scenes);
                var err = P1_5_SceneRestorePlanPhase.Execute(
                    EnabledDef(), view, out var requests);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(requests.Length, Is.EqualTo(1));
                Assert.That(requests[0].Guid.Hi, Is.EqualTo(0xAAUL));
                Assert.That(requests[0].Guid.Lo, Is.EqualTo(0xBBUL));
                Assert.That(requests[0].TargetState, Is.EqualTo(SceneLoadState.Loaded));
                Assert.That(requests[0].Mode, Is.EqualTo(SceneRestoreMode.Optional));
            }
            finally { buffer.Dispose(); }
        }

        [Test]
        public void MultipleScenes_AllRequestsInOrder()
        {
            var buffer = new NativeArray<byte>(256, Allocator.TempJob);
            try
            {
                var scenes = new SceneLoadInfo[]
                {
                    new SceneLoadInfo(new SceneGuid(1, 1), SceneLoadState.Loaded, SceneRestoreMode.Required),
                    new SceneLoadInfo(new SceneGuid(2, 2), SceneLoadState.Unloaded, SceneRestoreMode.Optional),
                    new SceneLoadInfo(new SceneGuid(3, 3), SceneLoadState.Loading, SceneRestoreMode.Required),
                };
                var view = MakeView(buffer, scenes);
                var err = P1_5_SceneRestorePlanPhase.Execute(
                    EnabledDef(), view, out var requests);

                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(requests.Length, Is.EqualTo(3));
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(requests[i].Guid, Is.EqualTo(scenes[i].Guid));
                    Assert.That(requests[i].TargetState, Is.EqualTo(scenes[i].State));
                    Assert.That(requests[i].Mode, Is.EqualTo(scenes[i].RestoreMode));
                }
            }
            finally { buffer.Dispose(); }
        }
    }
}
