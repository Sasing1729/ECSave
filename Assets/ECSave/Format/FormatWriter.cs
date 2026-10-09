using System;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Format
{
    /// <summary>
    /// SaveData を書き込む（内部設計 6章）。バッファは呼び出し側が確保する。
    /// ヘッダ・セクション表の後書きパッチのため、ベースポインタと現在位置を保持する。
    /// </summary>
    public unsafe struct FormatWriter
    {
        private byte* _base;
        private int _capacity;
        private int _offset;

        public FormatWriter(byte* ptr, int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (ptr == null && capacity > 0) throw new ArgumentNullException(nameof(ptr));
            _base = ptr;
            _capacity = capacity;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Capacity => _capacity;

        private void Ensure(int size)
        {
            long end = (long)_offset + size;
            if (end > _capacity)
                throw new FormatException(FormatError.CorruptedData,
                    $"write out of range: offset={_offset}, size={size}, capacity={_capacity}");
        }

        public void Seek(int position)
        {
            if (position < 0 || position > _capacity)
                throw new FormatException(FormatError.CorruptedData, $"seek out of range: {position}");
            _offset = position;
        }

        // ── 基本書き込み（LE 固定、6章） ────────────

        public void WriteU8(byte v) { Ensure(1); _base[_offset++] = v; }

        public void WriteU16(ushort v)
        {
            Ensure(2);
            byte* p = _base + _offset;
            p[0] = (byte)v; p[1] = (byte)(v >> 8);
            _offset += 2;
        }

        public void WriteU32(uint v)
        {
            Ensure(4);
            byte* p = _base + _offset;
            p[0] = (byte)v; p[1] = (byte)(v >> 8);
            p[2] = (byte)(v >> 16); p[3] = (byte)(v >> 24);
            _offset += 4;
        }

        public void WriteU64(ulong v)
        {
            Ensure(8);
            byte* p = _base + _offset;
            p[0] = (byte)v; p[1] = (byte)(v >> 8);
            p[2] = (byte)(v >> 16); p[3] = (byte)(v >> 24);
            p[4] = (byte)(v >> 32); p[5] = (byte)(v >> 40);
            p[6] = (byte)(v >> 48); p[7] = (byte)(v >> 56);
            _offset += 8;
        }

        public void WriteI32(int v) => WriteU32(unchecked((uint)v));

        public void WriteBytes(byte* src, int count)
        {
            if (count == 0) return;
            Ensure(count);
            UnsafeUtility.MemCpy(_base + _offset, src, count);
            _offset += count;
        }

        public void Zero(int count)
        {
            if (count == 0) return;
            Ensure(count);
            for (int i = 0; i < count; i++) _base[_offset + i] = 0;
            _offset += count;
        }

        /// <summary>8バイト境界などへアラインメントする（6.1、6.10）。</summary>
        public void AlignTo(int alignment)
        {
            int rem = _offset % alignment;
            if (rem != 0) Zero(alignment - rem);
        }

        // ── ヘッダ・セクション表（6.2、6.3） ────────

        public void WriteHeader(in SaveHeader header)
        {
            WriteU32(header.Magic);
            WriteU16(header.FormatVersion);
            WriteU16(header.HeaderFlags);
            WriteU32(header.HeaderSize);
            WriteI32(header.GameDataVersion);
            WriteI32(header.LogicalEntityCount);
            WriteI32(header.SectionCount);
            WriteU64(header.SectionTableOffset);
            WriteU64(header.TotalLength);
            WriteU32(header.NameHeapLength);
            WriteU16(header.WorldIdLength);
            WriteU16(header.Reserved);
        }

        /// <summary>ヘッダを先頭へ書き戻す（TotalLength 確定後）。</summary>
        public void PatchHeader(in SaveHeader header)
        {
            int saved = _offset;
            Seek(0);
            WriteHeader(header);
            Seek(saved);
        }

        public void WriteSectionEntry(in SectionEntry e)
        {
            WriteU32(e.SectionId);
            WriteU8((byte)e.Kind);
            WriteU8(e.Flags);
            WriteU16(e.NameLength);
            WriteU64(e.NameHash);
            WriteU64(e.SchemaHash);
            WriteU64(e.Offset);
            WriteU64(e.Length);
            WriteU32(e.NameOffset);
            WriteU32(e.Reserved);
        }

        /// <summary>セクション表の任意エントリを書き戻す（Encode 完了後）。</summary>
        public void PatchSectionEntry(int sectionTableOffset, int index, in SectionEntry e)
        {
            int saved = _offset;
            Seek(sectionTableOffset + index * ECSaveFormatConstants.SectionEntrySize);
            WriteSectionEntry(e);
            Seek(saved);
        }

        // ── 汎用パッチ ────────────────────────────

        public void PatchU32(int position, uint value)
        {
            int saved = _offset; Seek(position); WriteU32(value); Seek(saved);
        }

        public void PatchU64(int position, ulong value)
        {
            int saved = _offset; Seek(position); WriteU64(value); Seek(saved);
        }

        /// <summary>フッタ（チェックサム u64、6.6）。</summary>
        public void WriteFooter(ulong checksum)
        {
            WriteU64(checksum);
        }
    }
}
