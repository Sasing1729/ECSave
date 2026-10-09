namespace ECSave.Internal
{
    internal static class D3_VersionCheckPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            if (ctx.Header.GameDataVersion > ctx.Definition.GameDataVersion)
                return SaveLoadErrorCode.VersionMismatch;

            return SaveLoadErrorCode.None;
        }
    }
}
