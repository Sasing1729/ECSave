using ECSave;
using ECSave.Internal;
using ECSave.Internal.SubScene;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace ECSave.Tests.Runtime.SubScene
{
    internal sealed class DummyPrefabMigration : IPrefabMigration
    {
        public ulong OldPrefabSchemaHash => 1;
        public ulong NewPrefabSchemaHash => 2;
        public void Migrate(ref PrefabMigrationContext context) { }
    }

    [TestFixture]
    public sealed class SubSceneDefinitionTests
    {
        [Test]
        public void Empty_HasNoConfiguration()
        {
            var d = SubSceneDefinition.Empty;
            Assert.That(d.RegisterSceneLoadState, Is.False);
            Assert.That(d.RegisterDestroyedEntities, Is.False);
            Assert.That(d.HasSceneRestoreCallback, Is.False);
            Assert.That(d.WithSceneStateRollback, Is.False);
            Assert.That(d.ContentVerification, Is.EqualTo(ContentVerificationMode.None));
            Assert.That(d.StreamingConsistency, Is.EqualTo(StreamingConsistencyMode.TimeBased));
            Assert.That(d.TombstoneCapacity, Is.EqualTo(0));
            Assert.That(d.SceneStateProvider, Is.Null);
            Assert.That(d.SceneRestoreCallback, Is.Null);
            Assert.That(d.PrefabMigrations, Is.Empty);
            Assert.That(d.HasAnyConfiguration, Is.False);
        }

        [Test]
        public void HasAnyConfiguration_TrueWhenAnyFlag()
        {
            var d = new SubSceneDefinition(
                true, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                Array.Empty<PrefabMigrationStepBase>());
            Assert.That(d.HasAnyConfiguration, Is.True);
        }

        [Test]
        public void HasAnyConfiguration_TrueWhenTombstoneCapacity()
        {
            var d = new SubSceneDefinition(
                false, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                100, null, null,
                Array.Empty<PrefabMigrationStepBase>());
            Assert.That(d.HasAnyConfiguration, Is.True);
        }

        [Test]
        public void HasAnyConfiguration_TrueWhenContentVerification()
        {
            var d = new SubSceneDefinition(
                false, false, false, false,
                ContentVerificationMode.GuidOnly,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                Array.Empty<PrefabMigrationStepBase>());
            Assert.That(d.HasAnyConfiguration, Is.True);
        }

        [Test]
        public void HasAnyConfiguration_TrueWhenPrefabMigrations()
        {
            var d = new SubSceneDefinition(
                false, false, false, false,
                ContentVerificationMode.None,
                StreamingConsistencyMode.TimeBased,
                0, null, null,
                new PrefabMigrationStepBase[] { new DummyStep() });
            Assert.That(d.HasAnyConfiguration, Is.True);
        }

        private sealed class DummyStep : PrefabMigrationStepBase
        {
            public override ulong OldPrefabSchemaHash => 1;
            public override ulong NewPrefabSchemaHash => 2;
            public override void Migrate(ref PrefabMigrationContext context) { }
        }
    }

    [TestFixture]
    public sealed class SubSceneDefinitionBuilderImplTests
    {
        [Test]
        public void Build_Defaults()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            var d = b.Build();
            Assert.That(d.RegisterSceneLoadState, Is.False);
            Assert.That(d.HasAnyConfiguration, Is.False);
        }

        [Test]
        public void RegisterSceneLoadState_SetsFlag()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*RegisterSceneLoadState is set but neither.*"));

            var b = new SubSceneDefinitionBuilderImpl();
            b.RegisterSceneLoadState();
            Assert.That(b.Build().RegisterSceneLoadState, Is.True);
        }

        [Test]
        public void RegisterDestroyedEntities_SetsFlag()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*RegisterDestroyedEntities is set but no identity type.*"));

            var b = new SubSceneDefinitionBuilderImpl();
            b.RegisterDestroyedEntities();
            Assert.That(b.Build().RegisterDestroyedEntities, Is.True);
        }

        [Test]
        public void MarkSceneRestoreCallbackDeclared_SetsFlag()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.MarkSceneRestoreCallbackDeclared();
            Assert.That(b.Build().HasSceneRestoreCallback, Is.True);
        }

        [Test]
        public void WithSceneRestoreCallback_SetsFlagAndDelegate()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            Func<SceneLoadRequest[], SceneLoadOutcome[]> cb = _ => Array.Empty<SceneLoadOutcome>();
            b.WithSceneRestoreCallback(cb);
            var d = b.Build();
            Assert.That(d.HasSceneRestoreCallback, Is.True);
            Assert.That(d.SceneRestoreCallback, Is.SameAs(cb));
        }

        [Test]
        public void WithSceneStateProvider_SetsDelegate()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            Func<IReadOnlyList<SceneLoadInfo>> provider = () => Array.Empty<SceneLoadInfo>();
            b.WithSceneStateProvider(provider);
            Assert.That(b.Build().SceneStateProvider, Is.SameAs(provider));
        }

        [Test]
        public void WithSceneStateRollback_SetsFlag()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.WithSceneStateRollback();
            Assert.That(b.Build().WithSceneStateRollback, Is.True);
        }

        [Test]
        public void WithTombstoneCapacity_Zero_SetsValue()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.WithTombstoneCapacity(0);
            Assert.That(b.Build().TombstoneCapacity, Is.EqualTo(0));
        }

        [Test]
        public void WithTombstoneCapacity_Positive_SetsValue()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.WithTombstoneCapacity(500);
            Assert.That(b.Build().TombstoneCapacity, Is.EqualTo(500));
        }

        [Test]
        public void WithTombstoneCapacity_Negative_Throws()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            var ex = Assert.Throws<SaveRegistrationException>(() => b.WithTombstoneCapacity(-1));
            Assert.That(ex.Code, Is.EqualTo(RegistrationErrorCodes.NegativeTombstoneCapacity));
        }

        [Test]
        public void WithTombstoneCapacity_Conflicting_Throws()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.WithTombstoneCapacity(100);
            var ex = Assert.Throws<SaveRegistrationException>(() => b.WithTombstoneCapacity(200));
            Assert.That(ex.Code, Is.EqualTo(RegistrationErrorCodes.ConflictingSetting));
        }

        [Test]
        public void WithTombstoneCapacity_SameValue_Accepted()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.WithTombstoneCapacity(100);
            Assert.DoesNotThrow(() => b.WithTombstoneCapacity(100));
        }

        [TestCase(ContentVerificationMode.None)]
        [TestCase(ContentVerificationMode.GuidOnly)]
        [TestCase(ContentVerificationMode.GuidAndContentHash)]
        public void WithContentVerification_ValidMode_SetsValue(ContentVerificationMode mode)
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.WithContentVerification(mode);
            Assert.That(b.Build().ContentVerification, Is.EqualTo(mode));
        }

        [Test]
        public void WithContentVerification_InvalidMode_Throws()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            var ex = Assert.Throws<SaveRegistrationException>(
                () => b.WithContentVerification((ContentVerificationMode)99));
            Assert.That(ex.Code, Is.EqualTo(RegistrationErrorCodes.InvalidEnumValue));
        }

        [Test]
        public void WithContentVerification_Conflicting_Throws()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.WithContentVerification(ContentVerificationMode.GuidOnly);
            var ex = Assert.Throws<SaveRegistrationException>(
                () => b.WithContentVerification(ContentVerificationMode.GuidAndContentHash));
            Assert.That(ex.Code, Is.EqualTo(RegistrationErrorCodes.ConflictingSetting));
        }

        [TestCase(StreamingConsistencyMode.TimeBased)]
        [TestCase(StreamingConsistencyMode.FrameBoundary)]
        public void WithStreamingConsistency_ValidMode_SetsValue(StreamingConsistencyMode mode)
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.WithStreamingConsistency(mode);
            Assert.That(b.Build().StreamingConsistency, Is.EqualTo(mode));
        }

        [Test]
        public void WithStreamingConsistency_InvalidMode_Throws()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            var ex = Assert.Throws<SaveRegistrationException>(
                () => b.WithStreamingConsistency((StreamingConsistencyMode)99));
            Assert.That(ex.Code, Is.EqualTo(RegistrationErrorCodes.InvalidEnumValue));
        }

        [Test]
        public void RegisterPrefabMigrationStep_Null_Throws()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            Assert.Throws<ArgumentNullException>(() => b.RegisterPrefabMigrationStep(null));
        }

        [Test]
        public void RegisterPrefabMigrationStep_DifferentTypes_AllAdded()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.RegisterPrefabMigrationStep(new StepA());
            b.RegisterPrefabMigrationStep(new StepB());
            Assert.That(b.Build().PrefabMigrations.Count, Is.EqualTo(2));
        }

        [Test]
        public void RegisterPrefabMigrationStep_DuplicateType_Ignored()
        {
            var b = new SubSceneDefinitionBuilderImpl();
            b.RegisterPrefabMigrationStep(new StepA());
            b.RegisterPrefabMigrationStep(new StepA());
            Assert.That(b.Build().PrefabMigrations.Count, Is.EqualTo(1));
        }

        [Test]
        public void FluentChain_AllMethods()
        {
            // FluentChain は RegisterSceneLoadState と RegisterDestroyedEntities の両方を呼ぶため、
            // Build() 時に 2 つの Warning が発火する。順序は Build() 内の実装順（RegisterSceneLoadState が先）。
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*RegisterSceneLoadState is set but neither.*"));
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(".*RegisterDestroyedEntities is set but no identity type.*"));

            var b = new SubSceneDefinitionBuilderImpl();
            b.RegisterSceneLoadState()
             .RegisterDestroyedEntities()
             .WithTombstoneCapacity(100)
             .WithContentVerification(ContentVerificationMode.GuidAndContentHash)
             .WithStreamingConsistency(StreamingConsistencyMode.FrameBoundary)
             .WithSceneStateRollback();

            var d = b.Build();
            Assert.That(d.RegisterSceneLoadState, Is.True);
            Assert.That(d.RegisterDestroyedEntities, Is.True);
            Assert.That(d.TombstoneCapacity, Is.EqualTo(100));
            Assert.That(d.ContentVerification, Is.EqualTo(ContentVerificationMode.GuidAndContentHash));
            Assert.That(d.StreamingConsistency, Is.EqualTo(StreamingConsistencyMode.FrameBoundary));
            Assert.That(d.WithSceneStateRollback, Is.True);
        }

        private sealed class StepA : PrefabMigrationStepBase
        {
            public override ulong OldPrefabSchemaHash => 1;
            public override ulong NewPrefabSchemaHash => 2;
            public override void Migrate(ref PrefabMigrationContext context) { }
        }

        private sealed class StepB : PrefabMigrationStepBase
        {
            public override ulong OldPrefabSchemaHash => 3;
            public override ulong NewPrefabSchemaHash => 4;
            public override void Migrate(ref PrefabMigrationContext context) { }
        }
    }

    [TestFixture]
    public sealed class SubSceneRegistrationErrorCodesTests
    {
        [Test]
        public void Codes_AreUnique()
        {
            var codes = new[]
            {
                SubSceneRegistrationErrorCodes.ConfigDuplicate,
                SubSceneRegistrationErrorCodes.TombstoneWithoutIdentity,
                SubSceneRegistrationErrorCodes.ConflictingSetting,
                SubSceneRegistrationErrorCodes.NegativeTombstoneCapacity,
                SubSceneRegistrationErrorCodes.InvalidEnumValue,
                SubSceneRegistrationErrorCodes.MissingProvider,
            };
            var set = new HashSet<string>(codes);
            Assert.That(set.Count, Is.EqualTo(codes.Length));
        }

        [Test]
        public void Codes_HaveExpectedValues()
        {
            // 命名規約: すべて "ECSAVERNNN" 形式（英字 + 数字のみ）。
            // ハイフン等の記号はログ集約・監視・テスト検証で問題を起こすため禁止。
            // 値は RegistrationErrorCodes の SubScene ブロックと一致させること。
            Assert.That(SubSceneRegistrationErrorCodes.ConfigDuplicate, Is.EqualTo("ECSAVER020"));
            Assert.That(SubSceneRegistrationErrorCodes.TombstoneWithoutIdentity, Is.EqualTo("ECSAVER021"));
            Assert.That(SubSceneRegistrationErrorCodes.ConflictingSetting, Is.EqualTo("ECSAVER022"));
            Assert.That(SubSceneRegistrationErrorCodes.NegativeTombstoneCapacity, Is.EqualTo("ECSAVER023"));
            Assert.That(SubSceneRegistrationErrorCodes.InvalidEnumValue, Is.EqualTo("ECSAVER024"));
            Assert.That(SubSceneRegistrationErrorCodes.MissingProvider, Is.EqualTo("ECSAVER025"));
        }

        [Test]
        public void Codes_HaveNoHyphen()
        {
            // 再発防止: エラーコードにハイフン等の記号を含めないこと。
            // Roslyn の診断 ID 規約（^[a-zA-Z][a-zA-Z0-9]*$）に準拠させ、
            // 実行時例外コードと Roslyn 診断 ID を同じ形式で扱えるようにする。
            var codes = new[]
            {
                SubSceneRegistrationErrorCodes.ConfigDuplicate,
                SubSceneRegistrationErrorCodes.TombstoneWithoutIdentity,
                SubSceneRegistrationErrorCodes.ConflictingSetting,
                SubSceneRegistrationErrorCodes.NegativeTombstoneCapacity,
                SubSceneRegistrationErrorCodes.InvalidEnumValue,
                SubSceneRegistrationErrorCodes.MissingProvider,
            };
            foreach (var c in codes)
            {
                Assert.That(c, Does.Match(@"^[A-Za-z][A-Za-z0-9]*$"),
                    $"Error code '{c}' must contain only alphanumeric characters (no hyphens or symbols).");
            }
        }

        [Test]
        public void Codes_MatchRegistrationErrorCodes()
        {
            // SubSceneRegistrationErrorCodes は RegistrationErrorCodes のエイリアス。
            // 値の二重管理を避けるため、両者が一致していることを検証する。
            Assert.That(SubSceneRegistrationErrorCodes.ConfigDuplicate,
                Is.EqualTo(RegistrationErrorCodes.SubSceneConfigDuplicate));
            Assert.That(SubSceneRegistrationErrorCodes.TombstoneWithoutIdentity,
                Is.EqualTo(RegistrationErrorCodes.TombstoneWithoutIdentity));
            Assert.That(SubSceneRegistrationErrorCodes.ConflictingSetting,
                Is.EqualTo(RegistrationErrorCodes.ConflictingSetting));
            Assert.That(SubSceneRegistrationErrorCodes.NegativeTombstoneCapacity,
                Is.EqualTo(RegistrationErrorCodes.NegativeTombstoneCapacity));
            Assert.That(SubSceneRegistrationErrorCodes.InvalidEnumValue,
                Is.EqualTo(RegistrationErrorCodes.InvalidEnumValue));
            Assert.That(SubSceneRegistrationErrorCodes.MissingProvider,
                Is.EqualTo(RegistrationErrorCodes.MissingProvider));
        }
    }
}
