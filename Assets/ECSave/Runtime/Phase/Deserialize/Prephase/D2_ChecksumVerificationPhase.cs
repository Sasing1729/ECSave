using ECSave.Format;

namespace ECSave.Internal
{
    internal static unsafe class D2_ChecksumVerificationPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            int total = (int)ctx.Header.TotalLength;
            int checksumEnd = total - ECSaveFormatConstants.FooterSize;

            byte* p = ctx.View.Pointer;
            ulong stored = *(ulong*)(p + checksumEnd);
            ulong actual = ChecksumCalculator.Compute(new ByteView(p, checksumEnd));

            return stored == actual
                ? SaveLoadErrorCode.None
                : SaveLoadErrorCode.ChecksumMismatch;
        }
    }
}
