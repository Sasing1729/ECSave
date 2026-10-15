using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Internal
{
    internal static unsafe class SaveCompression
    {
        public const uint Magic = 0x5A534345;
        public const int EnvelopeSize = 12;
        public const byte CodecLz4 = 1;
        public const int MinCompressLength = 256;
        private const long MaxRatio = 255;

        private static uint ReadU32(byte[] d, int o)
            => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

        public static bool IsCompressed(byte[] d)
            => d != null && d.Length >= EnvelopeSize && ReadU32(d, 0) == Magic;

        public static byte[] Compress(byte[] raw)
        {
            if (raw == null || raw.Length < MinCompressLength) return raw;

            int bound = Lz4Block.MaxCompressedSize(raw.Length);
            using var tmp = new NativeArray<byte>(
                bound, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            int n;
            fixed (byte* s = raw)
                n = Lz4Block.Compress(s, raw.Length, (byte*)tmp.GetUnsafePtr(), bound);

            if (n < 0 || n + EnvelopeSize >= raw.Length) return raw;

            var result = new byte[n + EnvelopeSize];
            result[0] = 0x45;
            result[1] = 0x43;
            result[2] = 0x53;
            result[3] = 0x5A;
            result[4] = CodecLz4;
            uint len = (uint)raw.Length;
            result[8] = (byte)len;
            result[9] = (byte)(len >> 8);
            result[10] = (byte)(len >> 16);
            result[11] = (byte)(len >> 24);

            fixed (byte* d = result)
                UnsafeUtility.MemCpy(d + EnvelopeSize, tmp.GetUnsafePtr(), n);
            return result;
        }

        public static SaveLoadErrorCode TryReadRawLength(byte[] d, out int rawLength)
        {
            rawLength = 0;
            if (d[4] != CodecLz4) return SaveLoadErrorCode.VersionMismatch;
            if (d[5] != 0 || d[6] != 0 || d[7] != 0) return SaveLoadErrorCode.CorruptedData;

            uint len = ReadU32(d, 8);
            long comp = d.Length - EnvelopeSize;
            if (len < (uint)(ECSaveFormatConstants.HeaderFixedSize + ECSaveFormatConstants.FooterSize)
                || len > int.MaxValue
                || len > comp * MaxRatio + 64)
                return SaveLoadErrorCode.CorruptedData;

            rawLength = (int)len;
            return SaveLoadErrorCode.None;
        }

        public static bool Decompress(byte[] d, byte* dst, int rawLength)
        {
            fixed (byte* s = d)
                return Lz4Block.Decompress(s + EnvelopeSize, d.Length - EnvelopeSize, dst, rawLength);
        }

        public static SaveLoadErrorCode TryDecompress(byte[] data, out byte[] raw)
        {
            raw = data;
            if (!IsCompressed(data)) return SaveLoadErrorCode.None;

            var err = TryReadRawLength(data, out int len);
            if (err != SaveLoadErrorCode.None) return err;

            var buf = new byte[len];
            fixed (byte* p = buf)
            {
                if (!Decompress(data, p, len)) return SaveLoadErrorCode.CorruptedData;
            }

            raw = buf;
            return SaveLoadErrorCode.None;
        }
    }
}
