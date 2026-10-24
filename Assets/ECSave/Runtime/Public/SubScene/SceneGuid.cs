using System;
using ECSave.Format;
using FormatException = System.FormatException;

namespace ECSave
{
    public readonly struct SceneGuid : IEquatable<SceneGuid>
    {
        public readonly ulong Hi;
        public readonly ulong Lo;

        public SceneGuid(ulong hi, ulong lo)
        {
            Hi = hi;
            Lo = lo;
        }

        public static SceneGuid ParseHex(string hex32)
        {
            if (string.IsNullOrEmpty(hex32) || hex32.Length != 32)
                throw new FormatException(
                    $"SceneGuid requires exactly 32 hex characters, got '{hex32 ?? "<null>"}'");
            return new SceneGuid(ParseHex64(hex32, 0), ParseHex64(hex32, 16));
        }

        /// <summary>
        /// 文字列から決定的に SceneGuid を導出する。
        ///
        /// <para>
        /// 旧実装は <c>FnvUtf8</c> を内部に持っていたが、
        /// <see cref="SchemaHashCore.HashUtf8"/> と完全に同一仕様であるべきにも関わらず、
        /// サロゲートペアの扱いに差があった（3 byte で誤エンコード）。
        /// </para>
        /// <para>
        /// 共通実装に委譲する。入力に非 BMP 文字を含まない限り、ハッシュ値は旧実装と一致する。
        /// </para>
        /// </summary>
        public static SceneGuid FromString(string s)
        {
            if (string.IsNullOrEmpty(s)) return default;
            ulong hi = SchemaHashCore.HashUtf8(s);
            ulong lo = SchemaHashCore.HashUtf8(s, hi);
            return new SceneGuid(hi, lo);
        }

        public bool Equals(SceneGuid other) => Hi == other.Hi && Lo == other.Lo;
        public override bool Equals(object obj) => obj is SceneGuid g && Equals(g);
        public override int GetHashCode() => Hi.GetHashCode() ^ (Lo.GetHashCode() * 397);
        public static bool operator ==(SceneGuid a, SceneGuid b) => a.Equals(b);
        public static bool operator !=(SceneGuid a, SceneGuid b) => !a.Equals(b);
        public override string ToString() => $"{Hi:X16}{Lo:X16}";

        private static ulong ParseHex64(string s, int offset)
        {
            ulong v = 0;
            for (int i = 0; i < 16; i++)
            {
                char c = s[offset + i];

                uint digit;
                if (c >= '0' && c <= '9') digit = (uint)(c - '0');
                else if (c >= 'a' && c <= 'f') digit = (uint)(c - 'a' + 10);
                else if (c >= 'A' && c <= 'F') digit = (uint)(c - 'A' + 10);
                else throw new FormatException($"Invalid hex character: '{c}'");

                v = (v << 4) | digit;
            }
            return v;
        }
    }
}