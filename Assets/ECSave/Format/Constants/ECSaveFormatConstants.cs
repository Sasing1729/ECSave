// Runtime/Format/Constants/ECSaveFormatConstants.cs
namespace ECSave.Format
{
    /// <summary>
    /// バイナリフォーマットの定数（内部設計 6章）。
    /// FormatVersion 1 の凍結範囲（6.11節）に含まれる。
    /// </summary>
    public static class ECSaveFormatConstants
    {
        // ── Magic ────────────────────────────────
        public const uint Magic = 0x56534345; // "ECSV" LE: 'E','C','S','V' → 0x45,0x43,0x53,0x56
        public const int MagicBytes = 4;

        // ── 現行バージョン ────────────────────────
        public const ushort CurrentFormatVersion = 1;

        // ── ヘッダ (6.2) ─────────────────────────
        public const int HeaderFixedSize = 0x30; // 48 bytes

        // ── セクション表 (6.3) ────────────────────
        public const int SectionEntrySize = 48;

        // ── 上限 (6.9) ───────────────────────────
        public const int MaxSectionCount = 4096;
        public const int MaxNameLength = 1024;
        public const int MaxWorldIdLength = 1024;
        public const int MaxLogicalEntityCount = 10_000_000;
        public const int MaxFieldNameLength = 255;
        public const int MaxStructSize = 65_535; // u16

        // ── フッタ (6.6) ─────────────────────────
        public const int FooterSize = 8; // checksum u64

        // ── アラインメント ────────────────────────
        public const int SectionBodyAlignment = 8;
        public const int TypeDescriptorAlignment = 8;

        // ── セクション本体フラグ (6.4) ────────────
        public const uint SectionFlags_FixedLength = 1u << 0;
        public const uint SectionFlags_HasEnabledBits = 1u << 1;

        // ── セクション表 Flags (6.3) ──────────────
        public const byte SectionEntryFlags_HasTypeDescriptor = 1 << 0;

        // ── ヘッダ Flags (6.2) ────────────────────
        public const ushort HeaderFlags_ReservedMask = 0xFFFF; // 0 必須
    }
}
