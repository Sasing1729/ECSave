namespace ECSave.Format
{
    /// <summary>Identity / Component / Buffer / Shared の共通形式ヘッダ（6.4）。</summary>
    public struct CommonSectionHeader
    {
        public uint Count;
        public uint Flags;
        public uint Stride;

        public bool IsFixedLength => (Flags & ECSaveFormatConstants.SectionFlags_FixedLength) != 0;
        public bool HasEnabledBits => (Flags & ECSaveFormatConstants.SectionFlags_HasEnabledBits) != 0;
    }

    public static class CommonSectionFormat
    {
        public const int HeaderSize = 12; // Count + Flags + Stride

        public static CommonSectionHeader ReadHeader(ref ByteReader r)
        {
            return new CommonSectionHeader
            {
                Count = r.ReadU32(),
                Flags = r.ReadU32(),
                Stride = r.ReadU32(),
            };
        }

        public static void WriteHeader(ref ByteWriter w, in CommonSectionHeader h)
        {
            w.WriteU32(h.Count);
            w.WriteU32(h.Flags);
            w.WriteU32(h.Stride);
        }

        public static void ValidateFlags(uint flags, uint count)
        {
            const uint known = ECSaveFormatConstants.SectionFlags_FixedLength
                             | ECSaveFormatConstants.SectionFlags_HasEnabledBits;
            if ((flags & ~known) != 0)
                throw new FormatException(FormatError.CorruptedData,
                    $"unknown section flags 0x{flags:X8}");
            if (count > ECSaveFormatConstants.MaxLogicalEntityCount)
                throw new FormatException(FormatError.CorruptedData,
                    $"section count out of range: {count}");
        }
    }
}
