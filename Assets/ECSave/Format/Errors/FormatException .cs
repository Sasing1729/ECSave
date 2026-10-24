using System;

namespace ECSave.Format
{
    /// <summary>
    /// Format 層内部の例外。公開 API 境界で SaveLoadErrorCode に変換される（15.1節）。
    /// </summary>
    public sealed class FormatException : Exception
    {
        public FormatError Error { get; }

        public FormatException(FormatError error, string message) : base(message)
        {
            Error = error;
        }
    }
}
