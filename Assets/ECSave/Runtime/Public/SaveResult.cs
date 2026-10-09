// Runtime/Public/SaveResult.cs
namespace ECSave
{
    /// <summary>Serialize の結果（外部設計 13.1）。</summary>
    public readonly struct SaveResult
    {
        public bool IsSuccess { get; }
        public byte[] Data { get; }
        public SaveLoadErrorCode ErrorCode { get; }

        private SaveResult(bool success, byte[] data, SaveLoadErrorCode code)
        {
            IsSuccess = success;
            Data = data;
            ErrorCode = code;
        }

        public static SaveResult Success(byte[] data)
        {
            if (data == null) throw new System.ArgumentNullException(nameof(data));
            return new SaveResult(true, data, SaveLoadErrorCode.None);
        }

        public static SaveResult Failure(SaveLoadErrorCode code)
        {
            if (code == SaveLoadErrorCode.None)
                throw new System.ArgumentException("Failure requires non-None error code", nameof(code));
            return new SaveResult(false, null, code);
        }
    }
}
