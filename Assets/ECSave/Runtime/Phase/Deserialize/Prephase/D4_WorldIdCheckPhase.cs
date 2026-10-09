using System.Text;

namespace ECSave.Internal
{
    internal static unsafe class D4_WorldIdCheckPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            string expected = string.IsNullOrEmpty(ctx.Definition.WorldId)
                ? ctx.World.Name
                : ctx.Definition.WorldId;

            string actual = ctx.Header.WorldIdLength > 0
                ? Encoding.UTF8.GetString(ctx.Header.WorldIdPtr, ctx.Header.WorldIdLength)
                : string.Empty;

            return actual == expected
                ? SaveLoadErrorCode.None
                : SaveLoadErrorCode.WorldMismatch;
        }
    }
}
