using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Internal
{
    internal static unsafe class D6_MigrationPhase
    {
        private const int MaxMigrationChainLength = 32;
        private const int MaxScratchRecordSize = ECSaveFormatConstants.MaxStructSize;

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
            var stepChains = new MigrationStepBase[n][];
            var newStrides = new int[n];
            var sectionKinds = new SectionKind[n];

            for (int i = 0; i < n; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                ref var e = ref ctx.SectionTable.Entries[i];
                sectionKinds[i] = e.Kind;
                if (e.SchemaHash == m.SchemaHash) continue;

                ulong nameHash = SchemaHashCore.HashUtf8(m.FullTypeName);
                var chain = ResolveChain(table, nameHash, e.SchemaHash, m.SchemaHash);
                if (chain == null || chain.Length == 0)
                    return SaveLoadErrorCode.SchemaMismatch;

                if (chain.Length > 1)
                {
                    for (int s = 0; s < chain.Length; s++)
                    {
                        if (chain[s].NewRecordStride <= 0)
                            return SaveLoadErrorCode.MigrationFailed;
                    }
                }

                stepChains[i] = chain;
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
                // 型記述子を持つセクションのみ blockLen を読む。
                // SubSceneState / Tombstone / PrefabIdentity は Flags=0 で
                // 先頭 4 byte は Count であり、blockLen ではない。
                tdSizes[i] = e.HasTypeDescriptor
                    ? (int)(*(uint*)(ctx.View.Pointer + oldOff[i]))
                    : 0;
                if (tdSizes[i] < 0 || tdSizes[i] > oldLen[i])
                    return SaveLoadErrorCode.CorruptedData;

                if (stepChains[i] == null)
                    newBodySize[i] = oldLen[i] - tdSizes[i];
                else
                {
                    byte* oldBody = ctx.View.Pointer + oldOff[i] + tdSizes[i];
                    int oldBodyLen = oldLen[i] - tdSizes[i];
                    int computed = ComputeMigratedBodySize(
                        oldBody, oldBodyLen, newStrides[i], sectionKinds[i]);
                    if (computed < 0) return SaveLoadErrorCode.MigrationFailed;
                    newBodySize[i] = computed;
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

            // NameHeap の旧ファイル上の位置は、旧ファイルのセクション表直後。
            // HeaderSize は WorldId 直後の位置であり、表/パディングを含まないため
            // コピー元として不正（Review #2b）。
            long oldTableEnd = (long)ctx.Header.SectionTableOffset
                             + (long)n * ECSaveFormatConstants.SectionEntrySize;
            if (oldTableEnd < 0 || oldTableEnd + nameHeapLen > ctx.View.Length)
                return SaveLoadErrorCode.CorruptedData;

            UnsafeUtility.MemCpy(p, old, headerSize);
            *(ulong*)(p + 0x18) = (ulong)tableOffset;   // SectionTableOffset (Review #2c)
            *(ulong*)(p + 0x20) = (ulong)totalSize;     // TotalLength
            if (nameHeapLen > 0)
                UnsafeUtility.MemCpy(p + nameHeapOffset, old + oldTableEnd, nameHeapLen);

            for (int i = 0; i < n; i++)
            {
                ref var e = ref ctx.SectionTable.Entries[i];
                var ne = e;
                ne.Offset = (ulong)newOff[i];
                ne.Length = (ulong)newLen[i];
                if (stepChains[i] != null)
                {
                    var m = ctx.ModuleBySection[i];
                    ne.SchemaHash = m.SchemaHash;
                    // 移行後は旧名（RenamedFrom）ではなく現在の型名で解決できるよう、
                    // NameHash も新名で更新する。これを怠ると、移行済みデータを
                    // RenamedFrom 定義のない別バージョンで読んだ際に解決不能になる。
                    ne.NameHash = SchemaHashCore.HashUtf8(m.FullTypeName);
                }

                var w = new FormatWriter(
                    p + tableOffset + i * ECSaveFormatConstants.SectionEntrySize,
                    ECSaveFormatConstants.SectionEntrySize);
                w.WriteSectionEntry(ne);
            }

            bool needScratch = false;
            for (int i = 0; i < n; i++)
                if (stepChains[i] != null && stepChains[i].Length > 1) { needScratch = true; break; }

            NativeArray<byte> scratch = default;
            if (needScratch)
                scratch = new NativeArray<byte>(MaxScratchRecordSize * 2, Allocator.TempJob);

            try
            {
                byte* scratchPtr = needScratch ? (byte*)scratch.GetUnsafePtr() : null;

                for (int i = 0; i < n; i++)
                {
                    UnsafeUtility.MemCpy(p + newOff[i], old + oldOff[i], tdSizes[i]);

                    if (stepChains[i] == null)
                    {
                        UnsafeUtility.MemCpy(
                            p + newOff[i] + tdSizes[i],
                            old + oldOff[i] + tdSizes[i],
                            newBodySize[i]);
                    }
                    else
                    {
                        var err = ApplyMigration(
                            old + oldOff[i] + tdSizes[i], oldLen[i] - tdSizes[i],
                            p + newOff[i] + tdSizes[i], newBodySize[i],
                            newStrides[i], stepChains[i], sectionKinds[i], scratchPtr);
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
            }
            finally
            {
                if (scratch.IsCreated) scratch.Dispose();
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

        private static MigrationStepBase[] ResolveChain(
            MigrationTable table, ulong nameHash, ulong oldHash, ulong targetHash)
        {
            if (oldHash == targetHash) return System.Array.Empty<MigrationStepBase>();

            var chain = new System.Collections.Generic.List<MigrationStepBase>();
            ulong current = oldHash;

            for (int i = 0; i < MaxMigrationChainLength; i++)
            {
                var step = table.Resolve(nameHash, current);
                if (step == null) return null;
                chain.Add(step);
                current = step.NewSchemaHash;
                if (current == targetHash) return chain.ToArray();
            }
            return null;
        }

        private static int ComputeMigratedBodySize(
            byte* oldBody, int oldBodyLen, int newStride, SectionKind kind)
        {
            switch (kind)
            {
                case SectionKind.Component:
                case SectionKind.Shared:
                case SectionKind.Identity:
                    return ComputeFixedBodySize(oldBody, oldBodyLen, newStride);
                case SectionKind.Buffer:
                    return ComputeBufferBodySize(oldBody, oldBodyLen, newStride);
                case SectionKind.ChunkComponent:
                    return ComputeChunkBodySize(oldBody, oldBodyLen, newStride);
                default:
                    return -1;
            }
        }

        private static int ComputeFixedBodySize(byte* oldBody, int oldBodyLen, int newStride)
        {
            var r = new ByteReader(new ByteView(oldBody, oldBodyLen));
            uint cnt = r.ReadU32();
            uint flags = r.ReadU32();
            r.ReadU32();
            int count = (int)cnt;
            bool he = (flags & ECSaveFormatConstants.SectionFlags_HasEnabledBits) != 0;
            long sz = 12L + 4L * count + (long)count * newStride;
            if (he) sz += (cnt + 7) / 8;
            return sz > int.MaxValue ? -1 : (int)sz;
        }

        private static int ComputeBufferBodySize(byte* oldBody, int oldBodyLen, int newStride)
        {
            var r = new ByteReader(new ByteView(oldBody, oldBodyLen));
            uint cnt = r.ReadU32();
            uint flags = r.ReadU32();
            int oldStride = (int)r.ReadU32();
            int count = (int)cnt;
            bool he = (flags & ECSaveFormatConstants.SectionFlags_HasEnabledBits) != 0;

            r.Skip(count * 4);
            var roView = r.PeekView((count + 1) * 4);
            uint lastOff = ((uint*)roView.Pointer)[count];
            r.Skip((count + 1) * 4);

            int totalElements = oldStride > 0 ? (int)(lastOff / (uint)oldStride) : 0;
            long sz = 12L + 4L * count + 4L * (count + 1) + (long)totalElements * newStride;
            if (he) sz += (cnt + 7) / 8;
            return sz > int.MaxValue ? -1 : (int)sz;
        }

        private static int ComputeChunkBodySize(byte* oldBody, int oldBodyLen, int newStride)
        {
            var r = new ByteReader(new ByteView(oldBody, oldBodyLen));
            uint gc = r.ReadU32();
            uint flags = r.ReadU32();
            r.ReadU32();
            int groupCount = (int)gc;
            bool isFixed = (flags & ECSaveFormatConstants.SectionFlags_FixedLength) != 0;

            // 可変長 ChunkComponent は未対応（現実装は常に固定長を出力）
            if (!isFixed) return -1;

            int moBytes = (groupCount + 1) * 4;
            var moView = r.PeekView(moBytes);
            uint memberCount = ((uint*)moView.Pointer)[groupCount];
            r.Skip(moBytes);
            r.Skip((int)memberCount * 4);

            long sz = 12L + moBytes + (long)memberCount * 4 + (long)groupCount * newStride;
            return sz > int.MaxValue ? -1 : (int)sz;
        }

        private static SaveLoadErrorCode ApplyMigration(
            byte* oldBody, int oldBodyLen, byte* newBody, int newBodyLen,
            int newStride, MigrationStepBase[] chain, SectionKind kind, byte* scratch)
        {
            switch (kind)
            {
                case SectionKind.Component:
                case SectionKind.Shared:
                case SectionKind.Identity:
                    return ApplyFixedMigration(oldBody, oldBodyLen, newBody, newBodyLen, newStride, chain, scratch);
                case SectionKind.Buffer:
                    return ApplyBufferMigration(oldBody, oldBodyLen, newBody, newBodyLen, newStride, chain, scratch);
                case SectionKind.ChunkComponent:
                    return ApplyChunkMigration(oldBody, oldBodyLen, newBody, newBodyLen, newStride, chain, scratch);
                default:
                    return SaveLoadErrorCode.MigrationFailed;
            }
        }

        private static SaveLoadErrorCode ApplyFixedMigration(
            byte* oldBody, int oldBodyLen, byte* newBody, int newBodyLen,
            int newStride, MigrationStepBase[] chain, byte* scratch)
        {
            var r = new ByteReader(new ByteView(oldBody, oldBodyLen));
            uint cnt = r.ReadU32();
            uint flags = r.ReadU32();
            int oldStride = (int)r.ReadU32();
            int count = (int)cnt;
            bool he = (flags & ECSaveFormatConstants.SectionFlags_HasEnabledBits) != 0;

            var w = new ByteWriter(newBody, newBodyLen);
            w.WriteU32(cnt);
            w.WriteU32(flags);
            w.WriteU32((uint)newStride);

            int eiBytes = count * 4;
            if (eiBytes > 0)
            {
                var eiView = r.ConsumeView(eiBytes);
                w.WriteBytes(eiView.Pointer, eiBytes);
            }

            if (he)
            {
                int ebBytes = (count + 7) / 8;
                if (ebBytes > 0)
                {
                    var ebView = r.ConsumeView(ebBytes);
                    w.WriteBytes(ebView.Pointer, ebBytes);
                }
            }

            if (count == 0) return SaveLoadErrorCode.None;

            byte* oldRecs = oldStride > 0 ? r.ReadBytesPtr(count * oldStride) : null;

            for (int i = 0; i < count; i++)
            {
                byte* oldRec = oldRecs != null ? oldRecs + i * oldStride : null;
                byte* newRec = newBody + w.Offset;
                var err = ApplyChain(oldRec, oldStride, newRec, newStride, chain, scratch);
                if (err != SaveLoadErrorCode.None) return err;
                w.Skip(newStride);
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode ApplyBufferMigration(
            byte* oldBody, int oldBodyLen, byte* newBody, int newBodyLen,
            int newStride, MigrationStepBase[] chain, byte* scratch)
        {
            var r = new ByteReader(new ByteView(oldBody, oldBodyLen));
            uint cnt = r.ReadU32();
            uint flags = r.ReadU32();
            int oldStride = (int)r.ReadU32();
            int count = (int)cnt;
            bool he = (flags & ECSaveFormatConstants.SectionFlags_HasEnabledBits) != 0;

            var eiView = count > 0 ? r.ConsumeView(count * 4) : default;

            var oldRoView = r.ConsumeView((count + 1) * 4);
            uint* oldRo = (uint*)oldRoView.Pointer;

            byte* ebPtr = null;
            int ebBytes = he ? (count + 7) / 8 : 0;
            if (ebBytes > 0) ebPtr = r.ReadBytesPtr(ebBytes);

            int oldRecBytes = (int)oldRo[count];
            byte* oldRecs = oldRecBytes > 0 ? r.ReadBytesPtr(oldRecBytes) : null;

            var w = new ByteWriter(newBody, newBodyLen);
            w.WriteU32(cnt);
            w.WriteU32(flags);
            w.WriteU32((uint)newStride);

            if (count > 0)
                w.WriteBytes(eiView.Pointer, count * 4);

            uint accum = 0;
            w.WriteU32(0);
            for (int i = 0; i < count; i++)
            {
                int elementCount = oldStride > 0
                    ? (int)((oldRo[i + 1] - oldRo[i]) / (uint)oldStride)
                    : 0;
                accum += (uint)(elementCount * newStride);
                w.WriteU32(accum);
            }

            if (ebBytes > 0) w.WriteBytes(ebPtr, ebBytes);

            for (int i = 0; i < count; i++)
            {
                int elementCount = oldStride > 0
                    ? (int)((oldRo[i + 1] - oldRo[i]) / (uint)oldStride)
                    : 0;

                for (int k = 0; k < elementCount; k++)
                {
                    byte* src = oldRecs != null ? oldRecs + oldRo[i] + k * oldStride : null;
                    byte* dst = newBody + w.Offset;
                    var err = ApplyChain(src, oldStride, dst, newStride, chain, scratch);
                    if (err != SaveLoadErrorCode.None) return err;
                    w.Skip(newStride);
                }
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode ApplyChunkMigration(
            byte* oldBody, int oldBodyLen, byte* newBody, int newBodyLen,
            int newStride, MigrationStepBase[] chain, byte* scratch)
        {
            var r = new ByteReader(new ByteView(oldBody, oldBodyLen));
            uint gc = r.ReadU32();
            uint flags = r.ReadU32();
            int oldStride = (int)r.ReadU32();
            int groupCount = (int)gc;
            bool isFixed = (flags & ECSaveFormatConstants.SectionFlags_FixedLength) != 0;

            if (!isFixed) return SaveLoadErrorCode.MigrationFailed;

            int moBytes = (groupCount + 1) * 4;
            var moView = r.ConsumeView(moBytes);
            uint* mo = (uint*)moView.Pointer;
            uint memberCount = mo[groupCount];

            int meiBytes = (int)memberCount * 4;
            var meiView = meiBytes > 0 ? r.ConsumeView(meiBytes) : default;

            byte* oldRecs = groupCount > 0 ? r.ReadBytesPtr(groupCount * oldStride) : null;

            var w = new ByteWriter(newBody, newBodyLen);
            w.WriteU32(gc);
            w.WriteU32(flags);
            w.WriteU32((uint)newStride);
            w.WriteBytes(moView.Pointer, moBytes);
            if (meiBytes > 0) w.WriteBytes(meiView.Pointer, meiBytes);

            for (int g = 0; g < groupCount; g++)
            {
                byte* src = oldRecs != null ? oldRecs + g * oldStride : null;
                byte* dst = newBody + w.Offset;
                var err = ApplyChain(src, oldStride, dst, newStride, chain, scratch);
                if (err != SaveLoadErrorCode.None) return err;
                w.Skip(newStride);
            }

            return SaveLoadErrorCode.None;
        }

        private static SaveLoadErrorCode ApplyChain(
            byte* src, int srcLen, byte* dst, int dstLen, MigrationStepBase[] chain, byte* scratch)
        {
            if (chain == null || chain.Length == 0)
                return SaveLoadErrorCode.MigrationFailed;

            if (chain.Length == 1)
            {
                var r = new ByteReader(new ByteView(src, srcLen));
                var w = new ByteWriter(dst, dstLen);
                try { chain[0].Migrate(ref r, ref w); }
                catch { return SaveLoadErrorCode.MigrationFailed; }
                if (w.Offset != dstLen) return SaveLoadErrorCode.MigrationFailed;
                return SaveLoadErrorCode.None;
            }

            if (scratch == null) return SaveLoadErrorCode.MigrationFailed;

            byte* ping = scratch;
            byte* pong = scratch + MaxScratchRecordSize;

            byte* currentIn = src;
            int currentInLen = srcLen;
            byte* currentOut = ping;
            int currentOutLen = chain[0].NewRecordStride;

            for (int i = 0; i < chain.Length; i++)
            {
                if (currentOutLen <= 0 || currentOutLen > MaxScratchRecordSize)
                    return SaveLoadErrorCode.MigrationFailed;

                var rr = new ByteReader(new ByteView(currentIn, currentInLen));
                var ww = new ByteWriter(currentOut, currentOutLen);
                try { chain[i].Migrate(ref rr, ref ww); }
                catch { return SaveLoadErrorCode.MigrationFailed; }

                if (ww.Offset != currentOutLen) return SaveLoadErrorCode.MigrationFailed;

                if (i < chain.Length - 1)
                {
                    currentIn = currentOut;
                    currentInLen = currentOutLen;
                    currentOut = (currentOut == ping) ? pong : ping;
                    currentOutLen = chain[i + 1].NewRecordStride;
                }
            }

            if (currentOutLen != dstLen) return SaveLoadErrorCode.MigrationFailed;

            for (int b = 0; b < dstLen; b++)
                dst[b] = currentOut[b];

            return SaveLoadErrorCode.None;
        }

        private static int AlignUp(int v, int a)
        {
            int r = v % a;
            return r == 0 ? v : v + (a - r);
        }
    }
}
