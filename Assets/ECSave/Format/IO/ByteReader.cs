// Runtime/Format/IO/ByteReader.cs
using System;
using Unity.Collections;

namespace ECSave.Format
{
    /// <summary>
    /// LE 固定の読み取り（内部設計 3.1、6章）。すべての読み取りは ByteView の範囲内。
    /// </summary>
    public unsafe struct ByteReader
    {
        private ByteView _view;
        private int _offset;

        public ByteReader(ByteView view)
        {
            _view = view;
            _offset = 0;
        }

        public int Offset => _offset;
        public int Remaining => _view.Length - _offset;

        private void Ensure(int size)
        {
            if (!_view.BoundsCheck(_offset, size))
                throw new FormatException(FormatError.CorruptedData, $"read out of range: offset={_offset}, size={size}, total={_view.Length}");
        }

        public byte ReadU8()
        {
            Ensure(1);
            return _view.Pointer[_offset++];
        }

        public ushort ReadU16()
        {
            Ensure(2);
            byte* p = _view.Pointer + _offset;
            ushort v = (ushort)(p[0] | (p[1] << 8));
            _offset += 2;
            return v;
        }

        public uint ReadU32()
        {
            Ensure(4);
            byte* p = _view.Pointer + _offset;
            uint v = (uint)(p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24));
            _offset += 4;
            return v;
        }

        public ulong ReadU64()
        {
            Ensure(8);
            byte* p = _view.Pointer + _offset;
            ulong lo = (uint)(p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24));
            ulong hi = (uint)(p[4] | (p[5] << 8) | (p[6] << 16) | (p[7] << 24));
            _offset += 8;
            return lo | (hi << 32);
        }

        public int ReadI32() => unchecked((int)ReadU32());

        /// <summary>指定長のバイト列をコピーせずにポインタを返す（呼び出し側が範囲外アクセスしないこと）。</summary>
        public byte* ReadBytesPtr(int count)
        {
            Ensure(count);
            byte* p = _view.Pointer + _offset;
            _offset += count;
            return p;
        }

        public void Skip(int count)
        {
            Ensure(count);
            _offset += count;
        }

        /// <summary>現在位置からサブビューを作る（消費しない）。</summary>
        public ByteView PeekView(int length)
        {
            Ensure(length);
            return new ByteView(_view.Pointer + _offset, length);
        }

        public ByteView ConsumeView(int length)
        {
            Ensure(length);
            var v = new ByteView(_view.Pointer + _offset, length);
            _offset += length;
            return v;
        }

        /// <summary>8 バイト境界まで読み飛ばす（6.10）。</summary>
        public void AlignTo(int alignment)
        {
            int rem = _offset % alignment;
            if (rem != 0) Skip(alignment - rem);
        }
    }
}
