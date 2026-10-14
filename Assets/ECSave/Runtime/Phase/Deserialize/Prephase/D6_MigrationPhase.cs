using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Internal
{
    internal static unsafe class D6_MigrationPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            bool need = false;
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (ctx.SectionTable.Entries[i].SchemaHash != m.SchemaHash) { need = true; break; }
            }
            if (!need) return SaveLoadErrorCode.None;

            var table = ctx.Definition.Migrations;
            if (table == null) return SaveLoadErrorCode.SchemaMismatch;

            return Migrate(ref ctx, table);
        }

        private static SaveLoadErrorCode Migrate(ref PrephaseContext ctx, MigrationTable table)
        {
            int n = ctx.SectionTable.Count;
            var steps = new MigrationStepBase[n];
            var newStrides = new int[n];

            for (int i = 0; i < n; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                ref var e = ref ctx.SectionTable.Entries[i];
                if (e.SchemaHash == m.SchemaHash) continue;

                ulong nameHash = SchemaHashCore.HashUtf8(m.FullTypeName);
                var step = table.Resolve(nameHash, e.SchemaHash);
                if (step == null || step.NewSchemaHash != m.SchemaHash)
                    return SaveLoadErrorCode.SchemaMismatch;

                steps[i] = step;
                newStrides[i] = m.RecordStride;
            }

            var oldOff = new int[n];
            var oldLen = new int[n];
            var newOff = new int[n];
            var newLen = new int[n];
            var tdSizes = new int[n];
            var newBodySize = new int[n];

            for (int i = 0; i < n; i++)
            {
                ref var e = ref ctx.SectionTable.Entries[i];
                oldOff[i] = (int)e.Offset;
                oldLen[i] = (int)e.Length;
                tdSizes[i] = (int)(*(uint*)(ctx.View.Pointer + oldOff[i]));

                if (steps[i] == null)
                    newBodySize[i] = oldLen[i] - tdSizes[i];
                else
                {
                    var r = new ByteReader(new ByteView(
                        ctx.View.Pointer + oldOff[i] + tdSizes[i],
                        oldLen[i] - tdSizes[i]));
                    var h = CommonSectionFormat.ReadHeader(ref r);
                    int cnt = (int)h.Count;
                    bool he = (h.Flags & ECSaveFormatConstants.SectionFlags_HasEnabledBits) != 0;
                    long sz = 12 + 4L * cnt + (long)cnt * newStrides[i];
                    if (he) sz += (cnt + 7) / 8;
                    newBodySize[i] = (int)sz;
                }

                newLen[i] = tdSizes[i] + newBodySize[i];
            }

            int headerSize = ECSaveFormatConstants.HeaderFixedSize + ctx.Header.WorldIdLength;
            int tableOffset = AlignUp(headerSize, 8);
            int nameHeapOffset = tableOffset + n * ECSaveFormatConstants.SectionEntrySize;
            int nameHeapLen = (int)ctx.Header.NameHeapLength;
            int sectionsStart = AlignUp(nameHeapOffset + nameHeapLen, 8);

            int cursor = sectionsStart;
            for (int i = 0; i < n; i++)
            {
                newOff[i] = cursor;
                cursor = AlignUp(cursor + newLen[i], 8);
            }

            int footerOff = cursor;
            int totalSize = footerOff + ECSaveFormatConstants.FooterSize;

            var newData = new NativeArray<byte>(totalSize, Allocator.Persistent);
            byte* p = (byte*)newData.GetUnsafePtr();
            byte* old = ctx.View.Pointer;

            UnsafeUtility.MemCpy(p, old, headerSize);
            *(ulong*)(p + 0x20) = (ulong)totalSize;
            UnsafeUtility.MemCpy(p + nameHeapOffset, old + ctx.Header.HeaderSize, nameHeapLen);

            for (int i = 0; i < n; i++)
            {
                ref var e = ref ctx.SectionTable.Entries[i];
                var ne = e;
                ne.Offset = (ulong)newOff[i];
                ne.Length = (ulong)newLen[i];
                if (steps[i] != null) ne.SchemaHash = ctx.ModuleBySection[i].SchemaHash;

                var w = new FormatWriter(
                    p + tableOffset + i * ECSaveFormatConstants.SectionEntrySize,
                    ECSaveFormatConstants.SectionEntrySize);
                w.WriteSectionEntry(ne);
            }

            for (int i = 0; i < n; i++)
            {
                UnsafeUtility.MemCpy(p + newOff[i], old + oldOff[i], tdSizes[i]);

                if (steps[i] == null)
                    UnsafeUtility.MemCpy(
                        p + newOff[i] + tdSizes[i],
                        old + oldOff[i] + tdSizes[i],
                        newBodySize[i]);
                else
                {
                    var err = MigrateBody(
                        old + oldOff[i] + tdSizes[i], oldLen[i] - tdSizes[i],
                        p + newOff[i] + tdSizes[i], newStrides[i], steps[i]);
                    if (err != SaveLoadErrorCode.None)
                    {
                        newData.Dispose();
                        return err;
                    }
                }

                int end = newOff[i] + newLen[i];
                int next = i + 1 < n ? newOff[i + 1] : footerOff;
                for (int k = end; k < next; k++) p[k] = 0;
            }

            ulong ck = ChecksumCalculator.Compute(new ByteView(p, footerOff));
            *(ulong*)(p + footerOff) = ck;

            ctx.NativeData.Dispose();
            ctx.NativeData = newData;
            ctx.View = new ByteView(p, totalSize);

            var rr = new FormatReader(ctx.View);
            ctx.Header = rr.ReadHeader();
            ctx.SectionTable = rr.ReadSectionTable();

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode MigrateBody(
            byte* src, int srcLen, byte* dst, int newStride, MigrationStepBase step)
        {
            var r = new ByteReader(new ByteView(src, srcLen));
            uint cnt = r.ReadU32();
            uint flags = r.ReadU32();
            uint oldStride = r.ReadU32();
            int count = (int)cnt;
            bool he = (flags & ECSaveFormatConstants.SectionFlags_HasEnabledBits) != 0;

            int eiOff = r.Offset;
            int eiSz = count * 4;
            int ebOff = eiOff + eiSz;
            int ebSz = he ? (count + 7) / 8 : 0;
            int recOff = ebOff + ebSz;

            var w = new FormatWriter(dst, 12 + eiSz + ebSz + count * newStride);
            w.WriteU32(cnt);
            w.WriteU32(flags);
            w.WriteU32((uint)newStride);

            if (eiSz > 0)
            {
                UnsafeUtility.MemCpy(dst + w.Offset, src + eiOff, eiSz);
                w.Seek(w.Offset + eiSz);
            }
            if (ebSz > 0)
            {
                UnsafeUtility.MemCpy(dst + w.Offset, src + ebOff, ebSz);
                w.Seek(w.Offset + ebSz);
            }

            if (newStride <= 0 || cnt == 0)
                return SaveLoadErrorCode.None;

            byte* dstRec = dst + w.Offset;

            for (int i = 0; i < count; i++)
            {
                byte* oldRec = src + recOff + i * (int)oldStride;
                byte* newRec = dstRec + i * newStride;

                var rr = new ByteReader(new ByteView(oldRec, (int)oldStride));
                var ww = new ByteWriter(newRec, newStride);

                try { step.Migrate(ref rr, ref ww); }
                catch { return SaveLoadErrorCode.MigrationFailed; }
            }

            return SaveLoadErrorCode.None;
        }

        private static int AlignUp(int v, int a)
        {
            int r = v % a;
            return r == 0 ? v : v + (a - r);
        }
    }
}
