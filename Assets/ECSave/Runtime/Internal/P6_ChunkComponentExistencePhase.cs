using ECSave.Format;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// P6 (前半): ChunkComponent の「存在確保」フェーズ。
    ///
    /// <para>
    /// <b>2 フェーズ構成</b>: ChunkComponent の復元は次の 2 段階で行われる。
    /// <list type="number">
    ///   <item>本フェーズで各 Entity に ChunkComponent を付与する（存在確保のみ）</item>
    ///   <item><see cref="P6_ChunkComponentRestorePhase"/> で代表メンバーを通じて値を書き込む</item>
    /// </list>
    /// これは ChunkComponent がチャンク単位の値を持ち、書き込みにはメンバー Entity が
    /// 1 つ以上存在する必要があるため。
    /// </para>
    /// </summary>
    internal static class P6_ChunkComponentExistencePhase
    {
        public static void Execute(ref PrephaseContext ctx, EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (m.SectionKind != SectionKind.ChunkComponent) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                m.RestoreSection(bodyView, ctx.PersistentToEntity, em);
            }
        }
    }
}
