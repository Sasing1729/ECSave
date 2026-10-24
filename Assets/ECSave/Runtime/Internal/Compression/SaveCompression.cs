using ECSave.Format;
using System;
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

        // Review #12d: 圧縮爆弾対策の絶対上限。
        public const int MaxRawLength = 256 * 1024 * 1024;

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

        /// <summary>
        /// ポインタ入力からの圧縮（P-3）。
        ///
        /// <para>
        /// 旧実装は呼び出し側で <c>new byte[TotalSize]</c> を確保して書き込み、
        /// その後 <see cref="Compress(byte[])"/> で別の <c>byte[]</c> を作っていた。
        /// 大規模データでは中間 <c>byte[]</c> が LOH 圧力を生む。
        /// </para>
        /// <para>
        /// 本オーバーロードは生の <c>byte*</c> を入力に取り、圧縮が有益な場合は
        /// 直接圧縮結果を返す。中間配列は圧縮が有益でない場合のみ確保する。
        /// </para>
        /// </summary>
        public static byte[] Compress(byte* src, int srcLength)
        {
            if (src == null || srcLength <= 0)
                return Array.Empty<byte>();

            if (srcLength < MinCompressLength)
            {
                var raw = new byte[srcLength];
                fixed (byte* d = raw)
                    UnsafeUtility.MemCpy(d, src, srcLength);
                return raw;
            }

            int bound = Lz4Block.MaxCompressedSize(srcLength);
            using var tmp = new NativeArray<byte>(
                bound, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            int n = Lz4Block.Compress(src, srcLength, (byte*)tmp.GetUnsafePtr(), bound);

            if (n < 0 || n + EnvelopeSize >= srcLength)
            {
                var raw = new byte[srcLength];
                fixed (byte* d = raw)
                    UnsafeUtility.MemCpy(d, src, srcLength);
                return raw;
            }

            var result = new byte[n + EnvelopeSize];
            result[0] = 0x45;
            result[1] = 0x43;
            result[2] = 0x53;
            result[3] = 0x5A;
            result[4] = CodecLz4;
            uint len = (uint)srcLength;
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
                || len > (uint)MaxRawLength
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
