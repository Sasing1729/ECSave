using System;
using NUnit.Framework;
using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using ECSave.Phase.Deserialize.Apply;
using UnityEngine;
using UnityEngine.TestTools;

namespace ECSave.Tests.Runtime.SubScene
{
    [TestFixture]
    public sealed class P3_5_SceneRestoreExecutionPhaseTests
    {
        private static SubSceneDefinition MakeDef(
            Func<SceneLoadRequest[], SceneLoadOutcome[]> callback)
        {
            return new SubSceneDefinition(
                true, false, callback != null, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, callback,
                Array.Empty<PrefabMigrationStepBase>());
        }

        private static SceneLoadRequest[] MakeRequest(SceneRestoreMode mode)
        {
            return new SceneLoadRequest[]
            {
                new SceneLoadRequest(new SceneGuid(1, 2), SceneLoadState.Loaded, mode),
            };
        }

        [Test]
        public void NullDef_ReturnsNone()
        {
            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var err = P3_5_SceneRestoreExecutionPhase.Execute(null, null, ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void NullRequests_ReturnsNone()
        {
            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    MakeDef(_ => Array.Empty<SceneLoadOutcome>()), null, ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void EmptyRequests_ReturnsNone()
        {
            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    MakeDef(_ => Array.Empty<SceneLoadOutcome>()),
                    Array.Empty<SceneLoadRequest>(), ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void NullCallback_ReturnsNoneWithWarningLog()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    ".*Scene restore requested but no SceneRestoreCallback.*"));

            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    MakeDef(null), MakeRequest(SceneRestoreMode.Required), ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void CallbackThrows_ReturnsSceneLoadFailed()
        {
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex(".*SceneRestoreCallback threw.*"));

            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var def = MakeDef(_ => throw new InvalidOperationException("boom"));
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    def, MakeRequest(SceneRestoreMode.Required), ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.SceneLoadFailed));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void CallbackReturnsNull_ReturnsNone()
        {
            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var def = MakeDef(_ => null);
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    def, MakeRequest(SceneRestoreMode.Required), ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void AllSuccess_ReturnsNone()
        {
            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var def = MakeDef(reqs =>
                {
                    var outcomes = new SceneLoadOutcome[reqs.Length];
                    for (int i = 0; i < outcomes.Length; i++)
                        outcomes[i] = SceneLoadOutcome.Ok();
                    return outcomes;
                });
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    def, MakeRequest(SceneRestoreMode.Required), ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void RequiredFailure_ReturnsSceneLoadFailed()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*Required scene restore failed.*"));

            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var def = MakeDef(_ => new[] { SceneLoadOutcome.Fail("nope") });
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    def, MakeRequest(SceneRestoreMode.Required), ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.SceneLoadFailed));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void OptionalFailure_ReturnsNoneAndAddsWarning()
        {
            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var def = MakeDef(_ => new[] { SceneLoadOutcome.Fail("nope") });
                int before = warnings.Count;
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    def, MakeRequest(SceneRestoreMode.Optional), ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
                Assert.That(warnings.Count, Is.EqualTo(before + 1));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void MixedOutcomes_RequiredSuccessOptionalFailure_ReturnsNone()
        {
            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var def = MakeDef(reqs =>
                {
                    var outcomes = new SceneLoadOutcome[reqs.Length];
                    for (int i = 0; i < outcomes.Length; i++)
                        outcomes[i] = reqs[i].Mode == SceneRestoreMode.Required
                            ? SceneLoadOutcome.Ok()
                            : SceneLoadOutcome.Fail("optional failed");
                    return outcomes;
                });

                var requests = new SceneLoadRequest[]
                {
                    new SceneLoadRequest(new SceneGuid(1, 1), SceneLoadState.Loaded, SceneRestoreMode.Required),
                    new SceneLoadRequest(new SceneGuid(2, 2), SceneLoadState.Loaded, SceneRestoreMode.Optional),
                };
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    def, requests, ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.None));
            }
            finally { warnings.Dispose(); }
        }

        [Test]
        public void MixedOutcomes_RequiredFailure_ReturnsSceneLoadFailed()
        {
            // 修正: Error → Warning
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*Required scene restore failed.*"));

            var warnings = new WarningCollector(Unity.Collections.Allocator.TempJob);
            try
            {
                var def = MakeDef(reqs =>
                {
                    var outcomes = new SceneLoadOutcome[reqs.Length];
                    for (int i = 0; i < outcomes.Length; i++)
                        outcomes[i] = SceneLoadOutcome.Fail("fail");
                    return outcomes;
                });

                var requests = new SceneLoadRequest[]
                {
                    new SceneLoadRequest(new SceneGuid(1, 1), SceneLoadState.Loaded, SceneRestoreMode.Optional),
                    new SceneLoadRequest(new SceneGuid(2, 2), SceneLoadState.Loaded, SceneRestoreMode.Required),
                };
                var err = P3_5_SceneRestoreExecutionPhase.Execute(
                    def, requests, ref warnings);
                Assert.That(err, Is.EqualTo(SaveLoadErrorCode.SceneLoadFailed));
            }
            finally { warnings.Dispose(); }
        }
    }
}
