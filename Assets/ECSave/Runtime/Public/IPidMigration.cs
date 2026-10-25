namespace ECSave
{
    /// <summary>
    /// <c>Entity</c> フィールドを含む型の Migration。
    ///
    /// <para>
    /// <b>TOldPid / TNewPid</b>: 保存型の <c>Entity</c> を <c>uint</c>（pid）に
    /// 置換した「Pid 形」の型。<see cref="ISaveDefinitionBuilder.RegisterPidForm{TSave, TPid}"/>
    /// で保存型と関連付ける。Pid 形のサイズは保存型の <c>RecordStride</c>（data stride）と
    /// 一致する必要がある。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>PidForm に含められるフィールド</b>:
    /// <list type="bullet">
    ///   <item><c>Entity</c> — <c>uint</c> に置換する（pid）</item>
    ///   <item><c>FixedStringN</c> — <b>同じ型をそのまま使う</b></item>
    ///   <item><c>FixedListN&lt;T&gt;</c> — <b>同じ型をそのまま使う</b>
    ///         （<c>FixedList&lt;Entity&gt;</c> は SaveType 側で <c>ECSAVE048</c> により禁止）</item>
    ///   <item>primitive / nested struct — 同じ型をそのまま使う</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>FixedString / FixedList のサイズ・要素型を変更する Migration</b>
    /// （例: <c>FixedString32Bytes</c> → <c>FixedString64Bytes</c>）は、
    /// <c>Migrate</c> 内で明示的に変換すること:
    /// <code>
    /// new.Name = new FixedString64Bytes(old.Name);
    /// </code>
    /// PidForm のレイアウト検証は「SaveType と PidForm の対応」のみを検証し、
    /// 「V1 → V2 の変換」は <c>Migrate</c> 内のユーザーコードの責任。
    /// </para>
    /// <para>
    /// <b>レイアウト検証の診断</b>: PidForm と SaveType のレイアウト不一致は
    /// Source Generator がコンパイル時に検出する（<c>ECSAVE051</c> / <c>ECSAVE052</c>）。
    /// </para>
    /// </remarks>
    public interface IPidMigration<TOldPid, TNewPid>
        where TOldPid : unmanaged
        where TNewPid : unmanaged
    {
        TNewPid Migrate(in TOldPid old);
    }
}
