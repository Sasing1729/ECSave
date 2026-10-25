using NUnit.Framework;
using ECSave;

namespace ECSave.Tests.Runtime
{
    /// <summary>
    /// <see cref="PidConstants"/> の公開定数と、
    /// <see cref="ECSave.Format.EntityRefConstants"/> との整合性を検証する。
    ///
    /// <para>
    /// <b>削除したテスト</b>:
    /// <list type="bullet">
    ///   <item><c>RegisterPidForm_NoThrow</c> / <c>RegisterPidMigration_NoThrow</c>:
    ///         実行時 no-op は SG が処理するマーカーであり、公開契約ではない</item>
    ///   <item><c>Build_AfterPidRegistrations_Succeeds</c>: no-op 検証の派生。
    ///         実際の登録は SG 生成コード経由で行われる（SG テストでカバー）</item>
    /// </list>
    /// </para>
    /// </summary>
    public class PidFormRegistrationTests
    {
        [Test]
        public void PidConstants_NullRef_MatchesEntityRefConstants()
        {
            Assert.AreEqual(
                ECSave.Format.EntityRefConstants.NullRef,
                PidConstants.NullRef,
                "PidConstants.NullRef must equal EntityRefConstants.NullRef (both are 'no reference')");
        }

        [Test]
        public void PidConstants_UnresolvedRef_MatchesEntityRefConstants()
        {
            Assert.AreEqual(
                ECSave.Format.EntityRefConstants.UnresolvedRef,
                PidConstants.UnresolvedRef,
                "PidConstants.UnresolvedRef must equal EntityRefConstants.UnresolvedRef " +
                "(both are 'unresolved reference')");
        }
    }
}
