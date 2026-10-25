using ECSave.Format;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// P4: モジュール値適用フェーズ。
    ///
    /// <para>
    /// <b>責務</b>: Component / Buffer / Shared モジュールの値を Entity に適用し、
    /// Identity モジュールの識別キーを Entity に適用する。
    /// </para>
    /// <para>
    /// <b>unsafe 修飾</b>: <c>PrephaseContext.SectionTable.Entries</c> は
    /// <c>SectionEntry*</c>（アンマネージドポインタ）であり、
    /// <c>ref var entry = ref ctx.SectionTable.Entries[i]</c> はポインタの逆参照を
    /// 伴うため、クラス全体に <c>unsafe</c> が必須（C# 言語仕様 CS0214）。
    /// </para>
    /// </summary>
    internal static unsafe class P4_ModuleApplyPhase
    {
        public static void ExecuteNonChunk(ref PrephaseContext ctx, EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (m.SectionKind == SectionKind.ChunkComponent) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                m.RestoreSection(bodyView, ctx.PersistentToEntity, em);
            }
        }

        public static void ExecuteIdentity(ref PrephaseContext ctx, EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                // SectionTable.Entries は SectionEntry* のため unsafe コンテキスト必須。
                ref var entry = ref ctx.SectionTable.Entries[i];
                if (entry.Kind != SectionKind.Identity) continue;

                var im = ctx.Definition.FindIdentity(entry.NameHash);
                if (im == null) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                im.RestoreSection(bodyView, ctx.PersistentToEntity, em);
            }
        }
    }
}
