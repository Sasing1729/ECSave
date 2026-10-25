namespace ECSave
{
    /// <summary>
    /// アセット参照（<see cref="Unity.Entities.Content.WeakObjectReference{T}"/>）を
    /// 安定 ID（<c>ulong</c>）で保存する際の特殊値。
    ///
    /// <para>
    /// <see cref="PidConstants"/> のアセット版。pid と同様、
    /// 安定 ID でも「無効」「未解決」を表現する必要がある。
    /// </para>
    /// </summary>
    public static class AssetRefConstants
    {
        /// <summary>元の値が <c>default(WeakObjectReference&lt;T&gt;)</c>（無効）だったことを表す。</summary>
        public const ulong NullRef = 0UL;

        /// <summary>保存時に解決できなかった参照を表す（ロード時に既定値に置換）。</summary>
        public const ulong UnresolvedRef = ulong.MaxValue;
    }
}
