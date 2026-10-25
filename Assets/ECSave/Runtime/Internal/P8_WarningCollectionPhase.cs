using System;
using System.Collections.Generic;

namespace ECSave.Internal
{
    /// <summary>
    /// P8: 警告収集フェーズ。
    ///
    /// <para>
    /// <see cref="PrephaseContext.Warnings"/> に蓄積された <see cref="WarningRecord"/> を
    /// 公開 API の <see cref="LoadWarning"/> に変換する。型名マップを構築し、
    /// <c>ToLoadWarnings</c> に渡す。
    /// </para>
    /// <para>
    /// <b>best-effort</b>: 呼び出し元（<c>DeserializeOperation.DoApply</c>）が例外を捕捉し、
    /// 警告なしで成功扱いにする。ここで例外を投げても、データ適用済みの状態は変わらない。
    /// </para>
    /// </summary>
    internal static unsafe class P8_WarningCollectionPhase
    {
        public static List<LoadWarning> Execute(ref PrephaseContext ctx)
        {
            var warnings = new List<LoadWarning>();
            var typeNameMap = BuildTypeNameMap(ref ctx);
            ctx.Warnings.ToLoadWarnings(warnings, typeNameMap);
            return warnings;
        }

        private static Dictionary<uint, string> BuildTypeNameMap(ref PrephaseContext ctx)
        {
            var map = new Dictionary<uint, string>();
            var table = ctx.SectionTable;
            if (table.Entries == null) return map;

            for (int i = 0; i < table.Count; i++)
            {
                var m = ctx.ModuleBySection != null ? ctx.ModuleBySection[i] : null;
                if (m != null)
                {
                    map[(uint)i] = m.FullTypeName;
                    continue;
                }

                ref var entry = ref table.Entries[i];
                if (entry.NameLength == 0) continue;
                if (table.NameHeap == null) continue;

                long nameStart = (long)entry.NameOffset;
                long nameEnd = nameStart + entry.NameLength;
                if (nameStart < 0 || nameEnd > table.NameHeapLength) continue;

                try
                {
                    var name = System.Text.Encoding.UTF8.GetString(
                        table.NameHeap + nameStart, entry.NameLength);
                    if (!string.IsNullOrEmpty(name))
                        map[(uint)i] = name;
                }
                catch
                {
                }
            }

            return map;
        }
    }
}
