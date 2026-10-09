// Runtime/Format/IO/ByteWriter.cs
using System;
using Unity.Collections;

namespace ECSave.Format
{
    /// <summary>
    /// LE 固定の書き込み（内部設計 3.1、6章）。
    /// </summary>
    public unsafe struct ByteWriter
    {
        private byte* _ptr;
        private int _capacity;
        private int _offset;

        public ByteWriter(byte* ptr, int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ptr = ptr;
            _capacity = capacity;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Capacity => _capacity;
        public int Remaining => _capacity - _offset;

        private void Ensure(int size)
        {
            long end = (long)_offset + size;
            if (end > _capacity)
                throw new FormatException(FormatError.CorruptedData, $"write out of range: offset={_offset}, size={size}, capacity={_capacity}");
        }

        public void WriteU8(byte v)
        {
            Ensure(1);
            _ptr[_offset++] = v;
        }

        public void WriteU16(ushort v)
        {
            Ensure(2);
            byte* p = _ptr + _offset;
            p[0] = (byte)v;
            p[1] = (byte)(v >> 8);
            _offset += 2;
        }

        public void WriteU32(uint v)
        {
            Ensure(4);
            byte* p = _ptr + _offset;
            p[0] = (byte)v;
            p[1] = (byte)(v >> 8);
            p[2] = (byte)(v >> 16);
            p[3] = (byte)(v >> 24);
            _offset += 4;
        }

        public void WriteU64(ulong v)
        {
            Ensure(8);
            byte* p = _ptr + _offset;
            p[0] = (byte)v;
            p[1] = (byte)(v >> 8);
            p[2] = (byte)(v >> 16);
            p[3] = (byte)(v >> 24);
            p[4] = (byte)(v >> 32);
            p[5] = (byte)(v >> 40);
            p[6] = (byte)(v >> 48);
            p[7] = (byte)(v >> 56);
            _offset += 8;
        }

        public void WriteI32(int v) => WriteU32(unchecked((uint)v));

        public void WriteBytes(byte* src, int count)
        {
            Ensure(count);
            for (int i = 0; i < count; i++) _ptr[_offset + i] = src[i];
            _offset += count;
        }

        public void Zero(int count)
        {
            Ensure(count);
            for (int i = 0; i < count; i++) _ptr[_offset + i] = 0;
            _offset += count;
        }

        public void AlignTo(int alignment)
        {
            int rem = _offset % alignment;
            if (rem != 0) Zero(alignment - rem);
        }
    }
}
