using System;
using System.Collections.Generic;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Serialize;
using UnityEngine;
using UnityEngine.TestTools;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public sealed class S2_5_SubSceneCapturePhaseTests
    {
        private static SubSceneDefinition MakeDef(
            bool registerSceneLoadState,
            Func<IReadOnlyList<SceneLoadInfo>> provider = null)
        {
            return new SubSceneDefinition(
                registerSceneLoadState, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, provider, null,
                Array.Empty<PrefabMigrationStepBase>());
        }

        [Test]
        public void NullDef_NoSection()
        {
            var r = S2_5_SubSceneCapturePhase.Execute(null);
            Assert.That(r.HasSection, Is.False);
            Assert.That(r.SectionBodySize, Is.EqualTo(0));
        }

        [Test]
        public void RegisterSceneLoadStateFalse_NoSection()
        {
            var r = S2_5_SubSceneCapturePhase.Execute(MakeDef(false));
            Assert.That(r.HasSection, Is.False);
        }

        [Test]
        public void ProviderReturnsScenes_SectionWithData()
        {
            var scenes = new SceneLoadInfo[]
            {
        new SceneLoadInfo(new SceneGuid(1, 2), SceneLoadState.Loaded),
            };
            var r = S2_5_SubSceneCapturePhase.Execute(MakeDef(true, () => scenes));

            Assert.That(r.HasSection, Is.True);
            Assert.That(r.Scenes.Count, Is.EqualTo(1));
            // 修正: ComputeBodySize(scenes) に置換
            Assert.That(r.SectionBodySize, Is.EqualTo(SubSceneStateLayout.ComputeBodySize(scenes)));
        }

        [Test]
        public void ProviderReturnsNull_EmptySection()
        {
            var r = S2_5_SubSceneCapturePhase.Execute(MakeDef(true, () => null));

            Assert.That(r.HasSection, Is.True);
            Assert.That(r.Scenes.Count, Is.EqualTo(0));
            // 修正: 空配列を渡すか HeaderSize 直接指定
            Assert.That(r.SectionBodySize, Is.EqualTo(SubSceneStateLayout.HeaderSize));
        }

        [Test]
        public void NoProvider_EmptySectionAndWarning()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    ".*RegisterSceneLoadState is enabled but no SceneStateProvider.*"));

            var r = S2_5_SubSceneCapturePhase.Execute(MakeDef(true, null));
            Assert.That(r.HasSection, Is.True);
            Assert.That(r.Scenes.Count, Is.EqualTo(0));
        }

        [Test]
        public void ProviderThrows_EmptySectionAndErrorLog()
        {
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(".*SceneStateProvider threw.*"));

            Func<IReadOnlyList<SceneLoadInfo>> provider =
                () => throw new InvalidOperationException("boom");
            var r = S2_5_SubSceneCapturePhase.Execute(MakeDef(true, provider));

            Assert.That(r.HasSection, Is.True);
            Assert.That(r.Scenes.Count, Is.EqualTo(0));
        }
    }
}
