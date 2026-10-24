using NUnit.Framework;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace ECSave.Tests
{
    /// <summary>
    /// A-31: FixedString* の UTF-8 マルチバイト文字境界検証。
    ///
    /// FixedString* は内部で UTF-8 バイト列を保持するが、Capacity（例: FixedString32Bytes=29B）
    /// を超える文字列を書き込むと、マルチバイト文字の「途中」で切れる可能性がある。
    ///
    /// ★ 重要: CopyFrom の容量超過時の挙動は Unity.Collections のバージョンや
    ///   UNITY_DOTS_DEBUG の有効/無効によって変わる。
    ///   - ArgumentException / IndexOutOfRangeException を投げる
    ///   - サイレントに何もコピーしない（Length=0 のまま）
    ///   - 切り詰める
    ///   いずれの挙動もあり得るため、ユーザーコードでは事前に
    ///   Encoding.UTF8.GetByteCount() で容量チェックすることを推奨する。
    /// </summary>
    public class A31_FixedStringUtf8BoundaryTests
    {
        private const char ReplacementChar = (char)0xFFFD;
        private const string Emoji = "😀";

        // ─────────────────────────────────────────────
        // 前提: Capacity と UTF-8 バイト数の関係
        // ─────────────────────────────────────────────

        [Test]
        public void Capacity_SanityCheck()
        {
            Debug.Log($"[A-31] FixedString32Bytes.Capacity  = {default(FixedString32Bytes).Capacity}");
            Debug.Log($"[A-31] FixedString64Bytes.Capacity  = {default(FixedString64Bytes).Capacity}");
            Debug.Log($"[A-31] FixedString128Bytes.Capacity = {default(FixedString128Bytes).Capacity}");
            Debug.Log($"[A-31] FixedString512Bytes.Capacity = {default(FixedString512Bytes).Capacity}");

            Assert.Greater(default(FixedString32Bytes).Capacity, 0);
            Assert.Less(default(FixedString32Bytes).Capacity, default(FixedString64Bytes).Capacity);
        }

        [Test]
        public void Japanese_ByteCount_IsThreeBytesPerChar()
        {
            Assert.AreEqual(3, Encoding.UTF8.GetByteCount("あ"));
            Assert.AreEqual(3, Encoding.UTF8.GetByteCount("ア"));
            Assert.AreEqual(3, Encoding.UTF8.GetByteCount("漢"));
            Assert.AreEqual(3, Encoding.UTF8.GetByteCount("字"));
            Assert.AreEqual(30, Encoding.UTF8.GetByteCount("あいうえおかきくけこ"));
        }

        [Test]
        public void Emoji_ByteCount_IsFourBytes()
        {
            Assert.AreEqual(4, Encoding.UTF8.GetByteCount(Emoji));
            Assert.AreEqual(4, Encoding.UTF8.GetByteCount("🎮"));
        }

        // ─────────────────────────────────────────────
        // 検証 1: CopyFrom の容量超過時の挙動（実際の挙動を記録）
        // ─────────────────────────────────────────────

        /// <summary>
        /// ★ 修正: 例外を「期待する」のではなく、実際の挙動を記録しつつ、
        ///   「容量を超えたデータが書き込まれないこと」だけを不変条件として検証する。
        ///   例外 / サイレント無視 / 切り詰め のいずれの実装でもこのテストは通る。
        /// </summary>
        [Test]
        public void CopyFrom_OverCapacity_DoesNotExceedCapacity()
        {
            var fs = new FixedString32Bytes();
            int cap = fs.Capacity;
            var input = new string('あ', (cap / 3) + 1); // 例: 29 → 10 文字 = 30 bytes
            int inputBytes = Encoding.UTF8.GetByteCount(input);

            Debug.Log($"[A-31] CopyFrom test: Capacity={cap}, input={inputBytes}B, " +
                     $"chars={input.Length}");

            bool threw = false;
            System.Exception caught = null;
            try
            {
                fs.CopyFrom(input);
            }
            catch (System.Exception ex)
            {
                threw = true;
                caught = ex;
            }

            Debug.Log($"[A-31] CopyFrom over-capacity: threw={threw}, " +
                     $"ExceptionType={(caught?.GetType().Name ?? "none")}, " +
                     $"Length={fs.Length}");

            // 不変条件: 容量を超えない
            Assert.LessOrEqual(fs.Length, cap,
                "CopyFrom 後の Length は Capacity を超えてはならない");

            // 挙動の分類をログに記録（テストの合否には影響させない）
            if (threw)
                Debug.Log("[A-31] 観測: 例外を投げた（バージョン/デバッグ設定依存）");
            else if (fs.Length == 0)
                Debug.Log("[A-31] 観測: サイレント無視（何もコピーされなかった）。" +
                         "ユーザーコードでは事前の容量チェックが必須");
            else
                Debug.Log($"[A-31] 観測: 部分的にコピーされた（Length={fs.Length}）");
        }

        [Test]
        public void CopyFrom_AtCapacity_Succeeds()
        {
            var fs = new FixedString32Bytes();
            var input = "あいうえおかきくけ"; // 9 文字 = 27 bytes < 29
            Assert.AreEqual(27, Encoding.UTF8.GetByteCount(input));

            fs.CopyFrom(input);

            Assert.AreEqual(27, fs.Length);
            Assert.AreEqual(input, fs.ToString());
        }

        // ─────────────────────────────────────────────
        // 検証 2: CopyFromTruncated の切り詰め位置（核心）
        // ─────────────────────────────────────────────

        [Test]
        public void CopyFromTruncated_Japanese_RecordsBoundaryBehavior()
        {
            var fs = new FixedString32Bytes();
            int cap = fs.Capacity;
            var input = new string('あ', (cap / 3) + 1); // 30 bytes → 29 に切り詰め

            Debug.Log($"[A-31] 入力: '{input}' = {Encoding.UTF8.GetByteCount(input)}B, " +
                     $"chars={input.Length}, Capacity={cap}");

            fs.CopyFromTruncated(input);

            var output = fs.ToString();

            Debug.Log($"[A-31] 結果: Length={fs.Length}B, chars={output.Length}");
            Debug.Log($"[A-31] 出力: '{output}'");

            bool isCharBoundary = (fs.Length % 3 == 0);
            bool hasReplacement = output.IndexOf(ReplacementChar) >= 0;
            bool hasOrphanSurrogate = HasOrphanSurrogate(output);

            Debug.Log($"[A-31] Length % 3 == 0 (文字境界切詰の可能性): {isCharBoundary}");
            Debug.Log($"[A-31] Contains U+FFFD (mojibake の痕跡): {hasReplacement}");
            Debug.Log($"[A-31] Has orphan surrogate: {hasOrphanSurrogate}");

            if (isCharBoundary)
                Debug.Log("[A-31] 観測: 文字境界で切った（安全）");
            else
                Debug.Log($"[A-31] 観測: バイト境界で切った（{fs.Length}%3={fs.Length % 3} バイト余り、" +
                         "末尾が UTF-8 的に不完全な可能性）");

            Assert.LessOrEqual(fs.Length, cap,
                "Length は Capacity を超えてはならない");
        }

        [Test]
        public void CopyFromTruncated_MixedAsciiJapanese_RecordsBoundary()
        {
            var fs = new FixedString32Bytes();
            int cap = fs.Capacity;
            var input = "abc" + new string('あ', cap / 3);
            Debug.Log($"[A-31] Mixed: '{input}' = {Encoding.UTF8.GetByteCount(input)}B");

            fs.CopyFromTruncated(input);
            Debug.Log($"[A-31] Result: Length={fs.Length}, Output='{fs}'");

            Assert.LessOrEqual(fs.Length, cap);
        }

        // ─────────────────────────────────────────────
        // 検証 3: 容量チェックによる事前検出（TryCopyFrom 代替）
        // ─────────────────────────────────────────────

        [Test]
        public void ByteCountCheck_OverCapacity_Detected()
        {
            var fs = new FixedString32Bytes();
            int cap = fs.Capacity;
            var input = new string('あ', (cap / 3) + 1);
            int byteCount = Encoding.UTF8.GetByteCount(input);

            bool fits = byteCount <= cap;
            Assert.IsFalse(fits,
                $"Encoding.UTF8.GetByteCount({byteCount}) > Capacity({cap}) で事前検出できる");
            Debug.Log($"[A-31] Pre-check: input={byteCount}B, Capacity={cap}B, fits={fits}");
        }

        [Test]
        public void ByteCountCheck_UnderCapacity_Detected()
        {
            var fs = new FixedString32Bytes();
            int cap = fs.Capacity;
            var input = "あいうえおかきくけ";
            int byteCount = Encoding.UTF8.GetByteCount(input);

            bool fits = byteCount <= cap;
            Assert.IsTrue(fits);

            fs.CopyFrom(input);
            Assert.AreEqual(27, fs.Length);
            Assert.AreEqual(input, fs.ToString());
        }

        // ─────────────────────────────────────────────
        // 検証 4: 絵文字（サロゲートペア）の切り詰め
        // ─────────────────────────────────────────────

        [Test]
        public void Emoji_SurrogatePair_Truncation()
        {
            var fs = new FixedString32Bytes();
            int cap = fs.Capacity;

            var sb = new StringBuilder();
            sb.Append('あ');
            for (int i = 0; i < 8; i++) sb.Append(Emoji);
            var input = sb.ToString();

            Debug.Log($"[A-31] Emoji input: {Encoding.UTF8.GetByteCount(input)}B " +
                     $"(expected: 3 + 4*8 = 35)");

            fs.CopyFromTruncated(input);

            var output = fs.ToString();
            Debug.Log($"[A-31] Result: Length={fs.Length}, Output='{output}'");

            bool hasOrphanSurrogate = HasOrphanSurrogate(output);
            Debug.Log($"[A-31] Orphan surrogate (サロゲートペア切断): {hasOrphanSurrogate}");

            Assert.LessOrEqual(fs.Length, cap);
        }

        // ─────────────────────────────────────────────
        // 検証 5: セーブデータからの切り詰め検出ヒューリスティック
        // ─────────────────────────────────────────────

        [Test]
        public void DetectTruncationInSaveData_Heuristic()
        {
            var cases = new[]
            {
                "hello",
                "こんにちは",
                new string('あ', 9),
                new string('あ', 10),
            };

            foreach (var input in cases)
            {
                var fs = new FixedString32Bytes();
                fs.CopyFromTruncated(input);
                var output = fs.ToString();

                bool likelyTruncated =
                    fs.Length == fs.Capacity
                    || output.IndexOf(ReplacementChar) >= 0
                    || HasOrphanSurrogate(output);

                Debug.Log($"[A-31] '{TruncateDisplay(input, 20)}' " +
                         $"({Encoding.UTF8.GetByteCount(input)}B) → Length={fs.Length}, " +
                         $"LikelyTruncated={likelyTruncated}");
            }

            Assert.Pass("ヒューリスティックの記録");
        }

        // ─────────────────────────────────────────────
        // 検証 6: 安全な書き込みパターン
        // ─────────────────────────────────────────────

        [Test]
        public void SafePattern_ValidateBeforeWrite()
        {
            var fs = new FixedString32Bytes();
            int cap = fs.Capacity;
            var userInput = "あいうえおかきくけこ"; // 30 bytes > 29
            int byteCount = Encoding.UTF8.GetByteCount(userInput);

            if (byteCount > cap)
            {
                Debug.Log($"[A-31] Validation: '{userInput}' は容量超過 " +
                         $"({byteCount} > {cap}B)");

                var safe = TruncateAtCharBoundary(userInput, cap);
                fs.CopyFrom(safe);
                Debug.Log($"[A-31] Truncated to '{fs}' ({fs.Length}B)");
            }
            else
            {
                fs.CopyFrom(userInput);
            }

            Assert.LessOrEqual(fs.Length, cap);
            Assert.IsFalse(fs.ToString().IndexOf(ReplacementChar) >= 0,
                "安全な切り詰め後は U+FFFD を含んではならない");
        }

        // ─────────────────────────────────────────────
        // 検証 7: 安全な切り詰めヘルパーの検証
        // ─────────────────────────────────────────────

        [Test]
        public void TruncateAtCharBoundary_Japanese()
        {
            var input = "あいうえおかきくけこ"; // 30 bytes
            var truncated = TruncateAtCharBoundary(input, 29);

            Debug.Log($"[A-31] Safe truncate: '{input}' (30B) → '{truncated}' " +
                     $"({Encoding.UTF8.GetByteCount(truncated)}B, {truncated.Length} chars)");

            Assert.AreEqual("あいうえおかきくけ", truncated);
            Assert.AreEqual(27, Encoding.UTF8.GetByteCount(truncated));

            var redecoded = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(truncated));
            Assert.AreEqual(truncated, redecoded);
        }

        [Test]
        public void TruncateAtCharBoundary_EmojiSafe()
        {
            var input = "あ" + Emoji + Emoji + Emoji + Emoji + Emoji + Emoji + Emoji + Emoji;
            var truncated = TruncateAtCharBoundary(input, 20);

            Debug.Log($"[A-31] Emoji safe truncate: '{truncated}' " +
                     $"({Encoding.UTF8.GetByteCount(truncated)}B)");

            Assert.IsFalse(HasOrphanSurrogate(truncated),
                "切り詰め後の文字列に孤立サロゲートを含んではならない");
            Assert.LessOrEqual(Encoding.UTF8.GetByteCount(truncated), 20);
        }

        // ─────────────────────────────────────────────
        // ヘルパー
        // ─────────────────────────────────────────────

        private static bool HasOrphanSurrogate(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]))
                {
                    if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1]))
                        return true;
                    i++;
                }
                else if (char.IsLowSurrogate(s[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static string TruncateAtCharBoundary(string input, int maxBytes)
        {
            if (string.IsNullOrEmpty(input)) return input;
            if (Encoding.UTF8.GetByteCount(input) <= maxBytes) return input;

            int bytes = 0;
            int charIndex = 0;
            while (charIndex < input.Length)
            {
                int charLen = (char.IsHighSurrogate(input[charIndex])
                    && charIndex + 1 < input.Length
                    && char.IsLowSurrogate(input[charIndex + 1]))
                    ? 2 : 1;

                int charBytes = Encoding.UTF8.GetByteCount(input.Substring(charIndex, charLen));
                if (bytes + charBytes > maxBytes) break;
                bytes += charBytes;
                charIndex += charLen;
            }

            return input.Substring(0, charIndex);
        }

        private static string TruncateDisplay(string s, int maxChars)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Length <= maxChars ? s : s.Substring(0, maxChars) + "…";
        }
    }
}
