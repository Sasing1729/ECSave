using Unity.Entities;

namespace ECSave
{
    /// <summary>
    /// <c>IComponentData</c> 同士の Migration。
    ///
    /// <para>
    /// <b>TOld / TNew</b>: 保存対象の型。<see cref="ISaveDefinitionBuilder.RegisterMigration{TMigration}"/>
    /// で登録する。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>制約</b>: <typeparamref name="TOld"/> / <typeparamref name="TNew"/> は
    /// <c>Entity</c>、<c>FixedString*</c>、<c>FixedList*</c>、およびジェネリック型を
    /// 含むことができません（<c>ECSAVE030</c>）。
    /// </para>
    /// <para>
    /// これは Migration が D6 フェーズ（Entity 解決前）で実行されるため、
    /// <c>Entity</c> が未解決であり、<c>FixedString</c> / <c>FixedList</c> の
    /// 特殊レイアウトを <c>SizeOf&lt;T&gt;()</c> で扱えないことが理由です。
    /// </para>
    /// <para>
    /// <b>代替</b>: これらの型を含む場合は <see cref="IPidMigration{TOldPid, TNewPid}"/>
    /// を使用してください。PidForm 経由で <c>Entity</c> を <c>uint</c> に置換し、
    /// <c>FixedString</c> / <c>FixedList</c> は同じ型のまま保持できます。
    /// </para>
    /// </remarks>
    public interface IComponentMigration<TOld, TNew>
        where TOld : unmanaged, IComponentData
        where TNew : unmanaged, IComponentData
    {
        TNew Migrate(in TOld old);
    }
}
