using ECSave.Format;
using ECSave.Internal;
using System;
using Unity.Collections;
using Unity.Entities;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Phase.Deserialize.Apply
{
    internal static unsafe class P7_2_FixedStringScanPhase
    {
        public static SaveLoadErrorCode Execute(ref PrephaseContext ctx)
        {
            bool autoRepair = ctx.Definition.AutoRepairFixedString;

            for (int s = 0; s < ctx.SectionTable.Count; s++)
            {
                var m = ctx.ModuleBySection[s];
                if (m == null) continue;

                var singleFields = m.FixedStringFields;
                var listLeaves = m.FixedListPathLeaves;
                bool hasSingle = singleFields != null && singleFields.Length > 0;
                bool hasList = listLeaves != null && listLeaves.Length > 0;
                if (!hasSingle && !hasList) continue;

                ByteView bodyView;
                try { bodyView = SectionBodyHelper.GetSectionBody(ctx, s); }
                catch (FormatException) { continue; }

                // Entity 参照を含むレコードは dataStride レイアウトで書かれている。
                // FixedStringFieldInfo.Offset は raw レイアウト基準なので、
                // dataStride レイアウト用に変換する（1回だけ、セクション単位）。
                var refs = m.EntityRefOffsets;
                if (refs != null && refs.Length > 0)
                {
                    singleFields = ShiftSingleFieldInfos(singleFields, refs);
                    listLeaves = ShiftListLeafInfos(listLeaves, refs);
                }

                if (m.SectionKind == SectionKind.ChunkComponent)
                    ScanChunkSection(ref ctx, (uint)s, bodyView, autoRepair, singleFields, listLeaves);
                else
                    ScanCommonSection(ref ctx, (uint)s, bodyView, autoRepair, singleFields, listLeaves);
            }

            return SaveLoadErrorCode.None;
        }

        private static FixedStringFieldInfo[] ShiftSingleFieldInfos(
            FixedStringFieldInfo[] source, int[] refs)
        {
            if (source == null || source.Length == 0) return source;
            var result = new FixedStringFieldInfo[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var info = source[i];
                info.Offset = SectionLayoutHelper.RawToDataOffset(info.Offset, refs);
                result[i] = info;
            }
            return result;
        }

        private static FixedListPathLeafFieldInfo[] ShiftListLeafInfos(
            FixedListPathLeafFieldInfo[] source, int[] refs)
        {
            if (source == null || source.Length == 0) return source;
            var result = new FixedListPathLeafFieldInfo[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var info = source[i];
                // FixedList の内部オフセット（Step* / Leaf*）は FixedList 内で閉じるため
                // Entity 圧縮の影響を受けない。変換が必要なのは FirstContainerOffset のみ。
                info.FirstContainerOffset =
                    SectionLayoutHelper.RawToDataOffset(info.FirstContainerOffset, refs);
                result[i] = info;
            }
            return result;
        }

        private static void ScanCommonSection(
            ref PrephaseContext ctx, uint sectionId, ByteView bodyView, bool autoRepair,
            FixedStringFieldInfo[] singleFields, FixedListPathLeafFieldInfo[] listLeaves)
        {
            var v = CommonSectionReader.Read(bodyView);
            int count = (int)v.Header.Count;
            int stride = (int)v.Header.Stride;
            if (stride <= 0) return;

            for (int i = 0; i < count; i++)
            {
                byte* rec = v.Records + i * stride;
                uint pid = v.EntityIndex[i];
                Entity source = pid < (uint)ctx.PersistentToEntity.Length
                    ? ctx.PersistentToEntity[(int)pid] : Entity.Null;

                bool suspect = ScanRecord(rec, stride, autoRepair, singleFields, listLeaves);
                if (suspect)
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.FixedStringMayBeTruncated,
                        sectionId, (int)pid, source));
            }
        }

        private static void ScanChunkSection(
            ref PrephaseContext ctx, uint sectionId, ByteView bodyView, bool autoRepair,
            FixedStringFieldInfo[] singleFields, FixedListPathLeafFieldInfo[] listLeaves)
        {
            var chunkView = ChunkComponentSectionReader.Read(bodyView);
            int groupCount = (int)chunkView.Header.GroupCount;
            int stride = (int)chunkView.Header.Stride;
            if (stride <= 0) return;

            for (int g = 0; g < groupCount; g++)
            {
                byte* rec = chunkView.Records + g * stride;
                int mStart = (int)chunkView.MemberOffset[g];
                int mEnd = (int)chunkView.MemberOffset[g + 1];
                if (mStart >= mEnd) continue;

                uint pid = chunkView.MemberEntityIndex[mStart];
                Entity source = pid < (uint)ctx.PersistentToEntity.Length
                    ? ctx.PersistentToEntity[(int)pid] : Entity.Null;

                bool suspect = ScanRecord(rec, stride, autoRepair, singleFields, listLeaves);
                if (suspect)
                    ctx.Warnings.Add(WarningRecord.Create(
                        LoadWarningKind.FixedStringMayBeTruncated,
                        sectionId, (int)pid, source));
            }
        }

        private static bool ScanRecord(
            byte* rec, int stride, bool autoRepair,
            FixedStringFieldInfo[] singleFields, FixedListPathLeafFieldInfo[] listLeaves)
        {
            bool any = false;

            if (singleFields != null)
            {
                for (int f = 0; f < singleFields.Length; f++)
                    if (ScanSingle(rec, stride, singleFields[f], autoRepair)) any = true;
            }

            if (listLeaves != null)
            {
                for (int f = 0; f < listLeaves.Length; f++)
                    if (ScanListPath(rec, stride, listLeaves[f], autoRepair)) any = true;
            }

            return any;
        }

        private static bool ScanSingle(byte* rec, int stride, in FixedStringFieldInfo info, bool autoRepair)
        {
            if (info.Offset + 2 > stride) return false;

            byte* leaf = rec + info.Offset;
            ushort length = *(ushort*)leaf;
            if (length == 0) return false;

            byte* data = leaf + 2;
            bool suspect = false;
            int newLength = length;

            if (length > info.Capacity) { suspect = true; newLength = info.Capacity; }
            else if (length == info.Capacity && ContainsNonAscii(data, length)) suspect = true;

            int boundary = FindValidUtf8Boundary(data, newLength, info.Capacity);
            if (boundary < newLength) { suspect = true; newLength = boundary; }

            if (suspect && autoRepair && newLength < length)
                *(ushort*)leaf = (ushort)newLength;

            return suspect;
        }

        private static bool ScanListPath(
            byte* rec, int stride, in FixedListPathLeafFieldInfo info, bool autoRepair)
        {
            if (info.StepCount <= 0 || info.StepCount > 4) return false;
            return ScanRecursive(rec, stride, info, 0, info.FirstContainerOffset, autoRepair);
        }

        private static bool ScanRecursive(
            byte* rec, int stride,
            in FixedListPathLeafFieldInfo info,
            int stepIndex, int containerAbsOffset, bool autoRepair)
        {
            var step = GetStep(info, stepIndex);
            long listStartOff = (long)containerAbsOffset + step.ListOffset;
            if (listStartOff + 2 > stride) return false;
            if (listStartOff + step.ElementDataOffset > stride) return false;

            byte* listStart = rec + listStartOff;
            ushort listLength = *(ushort*)listStart;

            int effective = listLength;
            bool listWarning = false;
            if (listLength > step.Capacity) { effective = step.Capacity; listWarning = true; }

            bool leafWarning = false;

            for (int e = 0; e < effective; e++)
            {
                long elemOff = listStartOff + step.ElementDataOffset + (long)e * step.ElementStride;

                if (stepIndex == info.StepCount - 1)
                {
                    if (elemOff + info.LeafOffset + 2 > stride) break;
                    byte* leaf = rec + elemOff + info.LeafOffset;
                    if (ScanLeaf(leaf, info.LeafCapacity, autoRepair)) leafWarning = true;
                }
                else
                {
                    int nextContainer = (int)elemOff;
                    if (ScanRecursive(rec, stride, in info, stepIndex + 1, nextContainer, autoRepair))
                        leafWarning = true;
                }
            }

            if (listWarning && autoRepair && effective < listLength)
                *(ushort*)listStart = (ushort)effective;

            return listWarning || leafWarning;
        }

        private static bool ScanLeaf(byte* leaf, int capacity, bool autoRepair)
        {
            ushort length = *(ushort*)leaf;
            if (length == 0) return false;

            byte* data = leaf + 2;
            bool suspect = false;
            int newLength = length;

            if (length > capacity) { suspect = true; newLength = capacity; }
            else if (length == capacity && ContainsNonAscii(data, length)) suspect = true;

            int boundary = FindValidUtf8Boundary(data, newLength, capacity);
            if (boundary < newLength) { suspect = true; newLength = boundary; }

            if (suspect && autoRepair && newLength < length)
                *(ushort*)leaf = (ushort)newLength;

            return suspect;
        }

        private static FixedListTraversalStep GetStep(in FixedListPathLeafFieldInfo info, int index)
            => index switch
            {
                0 => info.Step0,
                1 => info.Step1,
                2 => info.Step2,
                3 => info.Step3,
                _ => default,
            };

        private static bool ContainsNonAscii(byte* data, int length)
        {
            for (int i = 0; i < length; i++)
                if (data[i] > 0x7F) return true;
            return false;
        }

        private static int FindValidUtf8Boundary(byte* data, int length, int capacity)
        {
            if (length <= 0) return 0;
            if (length > capacity) length = capacity;

            int i = length - 1;
            while (i >= 0 && (data[i] & 0xC0) == 0x80) i--;
            if (i < 0) return length;

            byte start = data[i];
            int expected;
            if ((start & 0x80) == 0x00) expected = 1;
            else if ((start & 0xE0) == 0xC0) expected = 2;
            else if ((start & 0xF0) == 0xE0) expected = 3;
            else if ((start & 0xF8) == 0xF0) expected = 4;
            else return length;

            return (length - i) < expected ? i : length;
        }
    }
}
