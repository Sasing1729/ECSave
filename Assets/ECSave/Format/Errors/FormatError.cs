// Runtime/Format/Errors/FormatError.cs
namespace ECSave.Format
{
    /// <summary>Format 層のエラー分類（公開 API の SaveLoadErrorCode に変換される）。</summary>
    public enum FormatError
    {
        None = 0,
        CorruptedData,        // 構造不正、範囲外、重複、上限超過、Magic 不正、認識不能
        VersionMismatch,      // FormatVersion / GameDataVersion が新しすぎる
        ChecksumMismatch,     // チェックサム不一致（Runtime が判定、Format は入出力のみ）
        ValidationFailed,     // 内容不正（bool 0/1 以外、容量超過等）
        EntityReferenceInvalid,
        SchemaMismatch,
    }
}
