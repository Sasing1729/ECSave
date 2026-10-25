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

        // 圧縮爆弾対策の絶対上限。
        public const int MaxRawLength = 256 * 1024 * 1024;

        private const long MaxRatio = 255;

        private static uint ReadU32(byte[] d, int o)
            => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

        public static bool IsCompressed(byte[] d)
            => d != null && d.Length >= EnvelopeSize && ReadU32(d, 0) == Magic;

        /// <summary>
        /// 配列入力からの圧縮。
        ///
        /// <para>
        /// <b>参照同一性の契約</b>: 圧縮が不要または有益でない場合、
        /// <b>入力配列そのもの</b>を返す。呼び出し側は
        /// <c>ReferenceEquals(input, output)</c> で「圧縮されなかった」ことを検出できる。
        /// </para>
        /// <para>
        /// <b>null</b>: <c>null</c> を返す（呼び出し側の null チェックと一貫）。
        /// </para>
        /// <para>
        /// <b>P2a での失敗</b>: 一度 <see cref="Compress(byte*, int)"/> へ委譲する形に
        /// リファクタリングしたが、参照同一性契約を壊したため元に戻した。
        /// 本メソッドと <see cref="Compress(byte*, int)"/> は異なる契約を持つため、
        /// 完全な共通化はできない（エンベロープ構築のみ <see cref="BuildEnvelope"/> に共通化）。
        /// </para>
        /// </summary>
        public static byte[] Compress(byte[] raw)
        {
            if (raw == null) return null;
            if (raw.Length < MinCompressLength) return raw;

            int bound = Lz4Block.MaxCompressedSize(raw.Length);
            using var tmp = new NativeArray<byte>(
                bound, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            int n;
            fixed (byte* s = raw)
                n = Lz4Block.Compress(s, raw.Length, (byte*)tmp.GetUnsafePtr(), bound);

            if (n < 0 || n + EnvelopeSize >= raw.Length) return raw;

            return BuildEnvelope((byte*)tmp.GetUnsafePtr(), n, raw.Length);
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
        /// <para>
        /// <b>参照同一性は保証されない</b>: 入力がポインタであるため、
        /// 非圧縮時は新しい配列を確保してコピーする。
        /// 参照同一性が必要な場合は <see cref="Compress(byte[])"/> を使うこと。
        /// </para>
        /// <para>
        /// <b>null</b>: <c>Array.Empty&lt;byte&gt;()</c> を返す（ポインタ版は呼び出し側が
        /// 空を渡すことは想定しないが、防御的に空配列を返す）。
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

            return BuildEnvelope((byte*)tmp.GetUnsafePtr(), n, srcLength);
        }

        /// <summary>
        /// 圧縮済みバイト列にエンベロープヘッダを付加した <c>byte[]</c> を生成する。
        /// </summary>
        private static byte[] BuildEnvelope(byte* compressed, int compressedLen, int rawLength)
        {
            var result = new byte[compressedLen + EnvelopeSize];
            result[0] = 0x45;
            result[1] = 0x43;
            result[2] = 0x53;
            result[3] = 0x5A;
            result[4] = CodecLz4;
            uint len = (uint)rawLength;
            result[8] = (byte)len;
            result[9] = (byte)(len >> 8);
            result[10] = (byte)(len >> 16);
            result[11] = (byte)(len >> 24);

            fixed (byte* d = result)
                UnsafeUtility.MemCpy(d + EnvelopeSize, compressed, compressedLen);
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
