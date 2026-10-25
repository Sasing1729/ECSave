using NUnit.Framework;
using ECSave;
using Unity.Entities;

namespace ECSave.Tests.Runtime
{
    // ── テスト用の保存型と Pid 形 ─────────────────────
    public struct PidTestSave : IComponentData
    {
        public Entity Owner;
        public int Value;
    }

    public struct PidTestSavePid
    {
        public uint Owner;
        public int Value;
    }

    public sealed class PidTestMigration : IPidMigration<PidTestSavePid, PidTestSavePid>
    {
        public PidTestSavePid Migrate(in PidTestSavePid old) => old;
    }

    /// <summary>
    /// Phase 1 の <c>RegisterPidForm</c> / <c>RegisterPidMigration</c> が
    /// 実行時 no-op であることを検証する。
    ///
    /// バリデーションはコンパイル時（Source Generator）で行われるため、
    /// 実行時は呼び出しが通ることだけを確認する。
    /// </summary>
    public class PidFormRegistrationTests
    {
        [Test]
        public void RegisterPidForm_NoThrow()
        {
            var impl = new SaveDefinitionBuilderImpl();
            Assert.DoesNotThrow(() =>
                impl.RegisterPidForm<PidTestSave, PidTestSavePid>());
        }

        [Test]
        public void RegisterPidMigration_NoThrow()
        {
            var impl = new SaveDefinitionBuilderImpl();
            Assert.DoesNotThrow(() =>
                impl.RegisterPidMigration<PidTestMigration>());
        }

        [Test]
        public void Build_AfterPidRegistrations_Succeeds()
        {
            var impl = new SaveDefinitionBuilderImpl();
            impl.RegisterPidForm<PidTestSave, PidTestSavePid>();
            impl.RegisterPidMigration<PidTestMigration>();

            Assert.DoesNotThrow(() => impl.Build());
        }

        [Test]
        public void PidConstants_NullRef_MatchesEntityRefConstants()
        {
            Assert.AreEqual(
                ECSave.Format.EntityRefConstants.NullRef,
                PidConstants.NullRef);
        }

        [Test]
        public void PidConstants_UnresolvedRef_MatchesEntityRefConstants()
        {
            Assert.AreEqual(
                ECSave.Format.EntityRefConstants.UnresolvedRef,
                PidConstants.UnresolvedRef);
        }
    }
}
