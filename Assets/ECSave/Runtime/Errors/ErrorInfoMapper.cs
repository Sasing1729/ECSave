using ECSave.Format;

namespace ECSave.Internal
{
    public static class ErrorInfoMapper
    {
        public static SaveLoadErrorCode Map(FormatError e)
        {
            switch (e)
            {
                case FormatError.CorruptedData: return SaveLoadErrorCode.CorruptedData;
                case FormatError.ChecksumMismatch: return SaveLoadErrorCode.ChecksumMismatch;
                case FormatError.VersionMismatch: return SaveLoadErrorCode.VersionMismatch;
                case FormatError.SchemaMismatch: return SaveLoadErrorCode.SchemaMismatch;
                case FormatError.ValidationFailed: return SaveLoadErrorCode.ValidationFailed;
                case FormatError.EntityReferenceInvalid: return SaveLoadErrorCode.EntityReferenceInvalid;
                default: return SaveLoadErrorCode.CorruptedData;
            }
        }
    }
}
