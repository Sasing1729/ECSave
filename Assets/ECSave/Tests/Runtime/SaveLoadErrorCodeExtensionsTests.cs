using System;
using System.Linq;
using NUnit.Framework;

namespace ECSave.Tests.Runtime
{
    // 注意: このファイルはテストクラスのみを含む。
    // 拡張メソッド本体 (SaveLoadErrorCodeExtensions) は
    // Runtime/Public/SaveLoadErrorCodeExtensions.cs にのみ存在させること。
    // 両方に定義すると CS0121（呼び出しが曖昧）が発生する。
    public class SaveLoadErrorCodeExtensionsTests
    {
        [Test]
        public void AllCodes_HaveNonEmptyDescription()
        {
            foreach (SaveLoadErrorCode code in Enum.GetValues(typeof(SaveLoadErrorCode)))
            {
                if (code == SaveLoadErrorCode.None) continue;
                var desc = code.GetDescription();
                Assert.IsFalse(string.IsNullOrEmpty(desc),
                    $"{code}: GetDescription() must be non-empty");
            }
        }

        [Test]
        public void AllCodes_HaveNonEmptyRemediation()
        {
            foreach (SaveLoadErrorCode code in Enum.GetValues(typeof(SaveLoadErrorCode)))
            {
                if (code == SaveLoadErrorCode.None) continue;
                var rem = code.GetRemediation();
                Assert.IsFalse(string.IsNullOrEmpty(rem),
                    $"{code}: GetRemediation() must be non-empty");
            }
        }

        [Test]
        public void AllCodes_DoNotUseFallbackDescription()
        {
            // GetDescription の switch は _ => "Unknown error." のフォールバックを持つ。
            // 新規 enum 値の追加時に switch への追加を忘れると、
            // AllCodes_HaveNonEmptyDescription は通ってしまう（フォールバックが非空のため）。
            // ここで「フォールバック値そのもの」を検出する。
            foreach (SaveLoadErrorCode code in Enum.GetValues(typeof(SaveLoadErrorCode)))
            {
                if (code == SaveLoadErrorCode.None) continue;
                Assert.AreNotEqual("Unknown error.", code.GetDescription(),
                    $"{code}: GetDescription() returned the fallback; " +
                    "add a case to the switch in SaveLoadErrorCodeExtensions.");
            }
        }

        [Test]
        public void None_ProducesEmptyMessage()
        {
            Assert.AreEqual(string.Empty, SaveLoadErrorCode.None.GetDescription());
            Assert.AreEqual(string.Empty, SaveLoadErrorCode.None.GetRemediation());
            Assert.AreEqual(string.Empty, SaveLoadErrorCode.None.GetUserFacingMessage());
        }

        [Test]
        public void UserFacingMessage_CombinesDescriptionAndRemediation()
        {
            var msg = SaveLoadErrorCode.ChecksumMismatch.GetUserFacingMessage();

            var desc = SaveLoadErrorCode.ChecksumMismatch.GetDescription();
            var rem = SaveLoadErrorCode.ChecksumMismatch.GetRemediation();

            StringAssert.Contains(desc, msg);
            StringAssert.Contains(rem, msg);
        }

        [Test]
        public void UserFacingMessage_ForChecksumMismatch_MentionsBackup()
        {
            var msg = SaveLoadErrorCode.ChecksumMismatch.GetUserFacingMessage();
            StringAssert.Contains("backup", msg.ToLowerInvariant());
        }

        [Test]
        public void UserFacingMessage_ForWorldMismatch_MentionsWorldName()
        {
            var msg = SaveLoadErrorCode.WorldMismatch.GetUserFacingMessage();
            StringAssert.Contains("world", msg.ToLowerInvariant());
        }
    }
}
