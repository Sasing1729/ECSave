// Runtime/Format/IO/ByteView.cs
using System;
using Unity.Collections;

namespace ECSave.Format
{
    /// <summary>
    /// 範囲チェック付きの読み取りビュー（内部設計 6.9）。
    /// すべての読み取りはこの型を通す。
    /// </summary>
    public readonly unsafe struct ByteView
    {
        private readonly byte* _ptr;
        public readonly int Length;

        public ByteView(byte* ptr, int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            _ptr = ptr;
            Length = length;
        }

        public byte* Pointer => _ptr;

        /// <summary>境界チェック（checked 演算、6.9）。</summary>
        public bool BoundsCheck(int offset, int length)
        {
            if (offset < 0 || length < 0) return false;
            // offset + length のオーバーフロー防止
            long end = (long)offset + length;
            return end <= Length;
        }

        /// <summary>サブビューを返す。範囲外は例外。</summary>
        public ByteView Slice(int offset, int length)
        {
            if (!BoundsCheck(offset, length))
                throw new FormatException(FormatError.CorruptedData, $"slice out of range: offset={offset}, length={length}, total={Length}");
            return new ByteView(_ptr + offset, length);
        }
    }
}
