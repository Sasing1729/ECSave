using System;

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

        public static SceneGuid FromString(string s)
        {
            if (string.IsNullOrEmpty(s)) return default;
            ulong hi = FnvUtf8(s, 0xcbf29ce484222325UL);
            ulong lo = FnvUtf8(s, hi);
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

        private static ulong FnvUtf8(string s, ulong seed)
        {
            const ulong Prime = 0x100000001b3UL;
            ulong h = seed;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x80) { h ^= (byte)c; h *= Prime; }
                else if (c < 0x800)
                {
                    h ^= (byte)(0xC0 | (c >> 6)); h *= Prime;
                    h ^= (byte)(0x80 | (c & 0x3F)); h *= Prime;
                }
                else
                {
                    h ^= (byte)(0xE0 | (c >> 12)); h *= Prime;
                    h ^= (byte)(0x80 | ((c >> 6) & 0x3F)); h *= Prime;
                    h ^= (byte)(0x80 | (c & 0x3F)); h *= Prime;
                }
            }
            return h;
        }
    }
}
