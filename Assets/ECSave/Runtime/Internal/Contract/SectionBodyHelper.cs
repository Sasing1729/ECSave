using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;

namespace ECSave.Internal
{
    internal static unsafe class SectionBodyHelper
    {
        public static void WriteFixedHeader(
            ref FormatWriter writer, int count, int dataStride, bool hasEnabled)
        {
            uint flags = ECSaveFormatConstants.SectionFlags_FixedLength;
            if (hasEnabled) flags |= ECSaveFormatConstants.SectionFlags_HasEnabledBits;

            writer.WriteU32((uint)count);
            writer.WriteU32(flags);
            writer.WriteU32((uint)dataStride);
        }

        public static void WriteVariableHeader(
            ref FormatWriter writer, int count, int dataStride, bool hasEnabled)
        {
            uint flags = 0;
            if (hasEnabled) flags |= ECSaveFormatConstants.SectionFlags_HasEnabledBits;

            writer.WriteU32((uint)count);
            writer.WriteU32(flags);
            writer.WriteU32((uint)dataStride);
        }

        public static void WriteEntityIndices(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int count = buffer.Count;
            if (count == 0) return;

            using var indices = new NativeArray<uint>(count, Allocator.TempJob);
            uint* idst = (uint*)indices.GetUnsafePtr();
            for (int i = 0; i < count; i++)
                idst[i] = (uint)idOf[buffer.Entities[i]];
            writer.WriteBytes((byte*)idst, count * 4);
        }

        public static void WriteEnabledBits(
            ref FormatWriter writer, in SnapshotBuffer buffer, int count)
        {
            if (count == 0) return;

            int byteCount = (count + 7) / 8;
            using var eb = new NativeArray<byte>(byteCount, Allocator.TempJob);
            byte* eptr = (byte*)eb.GetUnsafePtr();
            for (int b = 0; b < byteCount; b++) eptr[b] = 0;
            for (int i = 0; i < count; i++)
                if (buffer.EnabledStates[i] != 0)
                    eptr[i / 8] |= (byte)(1 << (i % 8));
            writer.WriteBytes(eptr, byteCount);
        }

        public static void WriteRecordOffsets(
            ref FormatWriter writer, in SnapshotBuffer buffer, int count, int dataStride)
        {
            using var offsets = new NativeArray<uint>(count + 1, Allocator.TempJob);
            uint* optr = (uint*)offsets.GetUnsafePtr();
            optr[0] = 0;
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                total += buffer.RecordLengths[i];
                optr[i + 1] = (uint)(total * dataStride);
            }
            writer.WriteBytes((byte*)optr, (count + 1) * 4);
        }

        public static bool UnpackEnabled(byte* enabledBits, int index)
            => (enabledBits[index / 8] & (1 << (index % 8))) != 0;

        public static ByteView SliceBody(ByteView sectionView)
        {
            var reader = new ByteReader(sectionView);
            uint descriptorSize = reader.ReadU32();
            if (descriptorSize > (uint)sectionView.Length)
                throw new FormatException(
                    FormatError.CorruptedData, "type descriptor size out of range");

            return sectionView.Slice(
                (int)descriptorSize, sectionView.Length - (int)descriptorSize);
        }

        public static ByteView GetSectionBody(ByteView file, in SectionEntry entry)
            => SliceBody(file.Slice((int)entry.Offset, (int)entry.Length));

        public static ByteView GetSectionBody(in PrephaseContext ctx, int sectionIndex)
            => GetSectionBody(ctx.View, in ctx.SectionTable.Entries[sectionIndex]);

        public static void EnumerateSectionEntities(
            ByteView view, ulong offset, ulong length,
            NativeArray<Entity> persistentToEntity,
            NativeParallelHashSet<Entity> output)
        {
            var bodyView = SliceBody(view.Slice((int)offset, (int)length));

            var v = CommonSectionReader.Read(bodyView);
            int count = (int)v.Header.Count;

            for (int k = 0; k < count; k++)
            {
                uint pid = v.EntityIndex[k];
                if (pid >= persistentToEntity.Length) continue;
                var e = persistentToEntity[(int)pid];
                if (e != Entity.Null) output.Add(e);
            }
        }
    }
}
