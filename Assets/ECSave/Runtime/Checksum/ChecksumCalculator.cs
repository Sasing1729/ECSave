using ECSave.Format;
using Unity.Collections;

namespace ECSave.Internal
{
    /// <summary>
    /// SaveData のチェックサム（6.6節、A-01 で語順確定）。
    /// xxHash3 64bit。ファイル先頭からフッタ直前まで。
    /// </summary>
    public static unsafe class ChecksumCalculator
    {
        /// <summary>
        /// ByteView 全体のチェックサムを計算する。
        /// 空入力でも xxHash3 の定義に従い、既知ベクタを返す（A-01）。
        /// </summary>
        public static ulong Compute(ByteView view)
        {
            byte* ptr = view.Pointer;
            if (ptr == null)
            {
                // 空配列を fixed した場合など、ポインタが null の可能性がある。
                // xxHash3 は length=0 ならポインタを参照しないが、
                // null チェックで弾く実装もあるため、有効なポインタを渡す。
                byte dummy = 0;
                ptr = &dummy;
            }

            var h = xxHash3.Hash64(ptr, view.Length);
            // A-01：uint2.x が下位、uint2.y が上位
            return (ulong)h.x | ((ulong)h.y << 32);
        }

        /// <summary>既知ベクタ：空入力（seed=0）は 0x2D06800538D394C2。</summary>
        public const ulong EmptyInputChecksum = 0x2D06800538D394C2UL;
    }
}
