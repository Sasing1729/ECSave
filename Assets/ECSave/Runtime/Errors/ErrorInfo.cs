// Runtime/Errors/ErrorInfo.cs
namespace ECSave.Internal
{
    /// <summary>
    /// Managed 側のエラー情報（15.1節）。公開 API 境界で SaveLoadErrorCode に変換される。
    /// </summary>
    public readonly struct ErrorInfo
    {
        public readonly SaveLoadErrorCode Code;
        public readonly string Message;

        public ErrorInfo(SaveLoadErrorCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public static ErrorInfo None => new ErrorInfo(SaveLoadErrorCode.None, null);
        public bool IsNone => Code == SaveLoadErrorCode.None;
    }
}
