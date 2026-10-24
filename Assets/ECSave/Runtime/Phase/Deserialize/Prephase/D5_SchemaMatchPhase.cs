using ECSave.Format;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class D5_SchemaMatchPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            var def = ctx.Definition;
            int count = ctx.SectionTable.Count;
            ctx.ModuleBySection = new SaveTypeModule[count];

            int moduleCount = def.Modules.Count;
            var hashes = new ulong[moduleCount];
            for (int k = 0; k < moduleCount; k++)
                hashes[k] = SchemaHashCore.HashUtf8(def.Modules[k].FullTypeName);

            var claimed = new bool[moduleCount];
            var table = def.Migrations;

            for (int i = 0; i < count; i++)
            {
                ref var entry = ref ctx.SectionTable.Entries[i];

                if (entry.Kind == SectionKind.Identity)
                {
                    var err = ValidateIdentitySection(ref ctx, i, in entry);
                    if (err != SaveLoadErrorCode.None) return err;
                    continue;
                }

                int idx = IndexOf(hashes, entry.NameHash);
                if (idx < 0 && table != null
                    && table.TryResolveRenamed(entry.NameHash, out ulong renamedHash))
                    idx = IndexOf(hashes, renamedHash);

                if (idx < 0)
                {
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.UnknownSectionIgnored,
                        (uint)i, -1, Entity.Null));
                    continue;
                }

                var m = def.Modules[idx];
                if (m.SectionKind != entry.Kind)
                    return SaveLoadErrorCode.SchemaMismatch;

                if (claimed[idx])
                    return SaveLoadErrorCode.SchemaMismatch;
                claimed[idx] = true;

                ctx.ModuleBySection[i] = m;
            }

            return SaveLoadErrorCode.None;
        }

        /// <summary>
        /// Identity セクションの検証（Review #3）。
        ///
        /// <para>
        /// Identity セクションは <c>ModuleBySection</c> に載らないため、
        /// D6/D7/D8 からは全てスキップされる。そのため以下をここで検証する:
        /// </para>
        /// <list type="number">
        ///   <item>SchemaHash の一致（Migration 対象外だが、リネーム検出は NameHash で行う）</item>
        ///   <item>Header.Stride == KeyStride（不一致だと P1 で範囲外読み取り）</item>
        ///   <item>構造検証（Count * Stride == recordsLength、Flags 検証）</item>
        ///   <item>EntityIndex の範囲（LogicalEntityCount 未満）</item>
        /// </list>
        /// </summary>
        private static SaveLoadErrorCode ValidateIdentitySection(
            ref PrephaseContext ctx, int sectionIndex, in SectionEntry entry)
        {
            var def = ctx.Definition;
            var im = def.FindIdentity(entry.NameHash);

            if (im == null)
            {
                ctx.Warnings.Add(WarningRecord.Create(
                    LoadWarningKind.UnknownSectionIgnored,
                    (uint)sectionIndex, -1, Entity.Null));
                return SaveLoadErrorCode.None;
            }

            // 1. SchemaHash check
            if (entry.SchemaHash != im.SchemaHash)
                return SaveLoadErrorCode.SchemaMismatch;

            // 2. Body を読み、Header フィールドを直接確認する。
            //    CommonSectionReader.Read は Stride 不整合時に CorruptedData を投げるため、
            //    まず Stride だけを読んで SchemaMismatch を優先させる（Review #3）。
            ByteView bodyView;
            try
            {
                bodyView = SectionBodyHelper.GetSectionBody(ctx, sectionIndex);
            }
            catch (FormatException)
            {
                return SaveLoadErrorCode.CorruptedData;
            }

            if (bodyView.Length < CommonSectionFormat.HeaderSize)
                return SaveLoadErrorCode.CorruptedData;

            var headerReader = new ByteReader(bodyView);
            headerReader.ReadU32();  // Count
            headerReader.ReadU32();  // Flags
            uint sectionStride = headerReader.ReadU32();

            if ((int)sectionStride != im.KeyStride)
                return SaveLoadErrorCode.SchemaMismatch;

            // 3. 構造検証（Count * Stride == recordsLength 等）
            CommonSectionView v;
            try
            {
                v = CommonSectionReader.Read(bodyView);
            }
            catch (FormatException)
            {
                return SaveLoadErrorCode.CorruptedData;
            }

            // 4. EntityIndex の範囲検証（D8 が Identity を skip するため、ここで行う）
            uint limit = (uint)ctx.Header.LogicalEntityCount;
            int count = (int)v.Header.Count;
            for (int k = 0; k < count; k++)
            {
                if (v.EntityIndex[k] >= limit)
                    return SaveLoadErrorCode.ValidationFailed;
            }

            return SaveLoadErrorCode.None;
        }

        private static int IndexOf(ulong[] hashes, ulong nameHash)
        {
            for (int i = 0; i < hashes.Length; i++)
                if (hashes[i] == nameHash) return i;
            return -1;
        }
    }
}
