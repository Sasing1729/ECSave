namespace ECSave.Format
{
    /// <summary>ChunkComponent セクションのヘッダ（6.4）。</summary>
    public struct ChunkComponentSectionHeader
    {
        public uint GroupCount;
        public uint Flags;
        public uint Stride;

        public bool IsFixedLength => (Flags & ECSaveFormatConstants.SectionFlags_FixedLength) != 0;
    }

    public static class ChunkComponentSectionFormat
    {
        public const int HeaderSize = 12; // GroupCount + Flags + Stride

        public static ChunkComponentSectionHeader ReadHeader(ref ByteReader r)
        {
            return new ChunkComponentSectionHeader
            {
                GroupCount = r.ReadU32(),
                Flags = r.ReadU32(),
                Stride = r.ReadU32(),
            };
        }

        public static void WriteHeader(ref ByteWriter w, in ChunkComponentSectionHeader h)
        {
            w.WriteU32(h.GroupCount);
            w.WriteU32(h.Flags);
            w.WriteU32(h.Stride);
        }

        /// <summary>Flags を検証する（6.4：bit0 のみ許可、bit1 は 0 必須）。</summary>
        public static void ValidateFlags(uint flags)
        {
            const uint known = ECSaveFormatConstants.SectionFlags_FixedLength;
            if ((flags & ~known) != 0)
                throw new FormatException(FormatError.CorruptedData,
                    $"unknown chunk component flags 0x{flags:X8}");
        }
    }
}
