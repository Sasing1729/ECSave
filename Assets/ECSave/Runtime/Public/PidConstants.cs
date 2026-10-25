namespace ECSave
{
    /// <summary>
    /// Pid 形（<c>IPidMigration&lt;TOldPid, TNewPid&gt;</c> の型引数）で
    /// <c>Entity</c> フィールドの代わりに使う <c>uint</c> フィールドの特殊値。
    ///
    /// <para>
    /// 保存データ上の <c>Entity</c> は 4 バイトの永続 ID（pid）として記録される。
    /// <see cref="SaveLoadErrorCode"/> の D6 フェーズ（Migration）では Entity が未解決
    /// （<c>PersistentToEntity[pid] == Entity.Null</c>）であるため、PidMigration は
    /// <c>uint</c> として pid を扱う。
    /// </para>
    /// <para>
    /// <see cref="Format.EntityRefConstants.NullRef"/> / <see cref="Format.EntityRefConstants.UnresolvedRef"/>
    /// と同値。ユーザーコードが <c>ECSave.Format</c> を参照しなくて済むよう、
    /// <c>ECSave</c> 名前空間に再エクスポートしている。
    /// </para>
    /// </summary>
    public static class PidConstants
    {
        /// <summary>元の値が <c>Entity.Null</c> だったことを表す pid。</summary>
        public const uint NullRef = 0xFFFFFFFFu;

        /// <summary>保存時に解決できなかった参照を表す pid（ロード時に <c>Entity.Null</c> に置換）。</summary>
        public const uint UnresolvedRef = 0xFFFFFFFEu;
    }
}
