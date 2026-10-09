using ECSave.Format;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class EntityRefCodec
    {
        public static void WriteRecordWithRefs(
            ref FormatWriter writer,
            byte* src,
            int[] refOffsets,
            int rawStride,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int srcOff = 0;
            for (int r = 0; r < refOffsets.Length; r++)
            {
                int refOff = refOffsets[r];
                int gap = refOff - srcOff;
                if (gap > 0)
                    writer.WriteBytes(src + srcOff, gap);

                Entity e = *(Entity*)(src + refOff);
                uint pid;
                if (e == Entity.Null)
                    pid = EntityRefConstants.NullRef;
                else if (idOf.TryGetValue(e, out int id))
                    pid = (uint)id;
                else
                    pid = EntityRefConstants.UnresolvedRef;

                writer.WriteU32(pid);
                srcOff = refOff + 8;
            }
            int remaining = rawStride - srcOff;
            if (remaining > 0)
                writer.WriteBytes(src + srcOff, remaining);
        }

        public static void ExpandRecord(
            byte* src,
            int dataStride,
            int[] refOffsets,
            int rawStride,
            NativeArray<Entity> persistentToEntity,
            byte* dst)
        {
            int dstOff = 0;
            int srcOff = 0;
            for (int r = 0; r < refOffsets.Length; r++)
            {
                int refOff = refOffsets[r];
                int gap = refOff - dstOff;
                for (int b = 0; b < gap; b++)
                    dst[dstOff + b] = src[srcOff + b];
                dstOff += gap;
                srcOff += gap;

                uint pid = *(uint*)(src + srcOff);
                Entity e;
                if (pid == EntityRefConstants.NullRef)
                    e = Entity.Null;
                else if (pid == EntityRefConstants.UnresolvedRef)
                    e = Entity.Null;
                else if (pid < (uint)persistentToEntity.Length)
                    e = persistentToEntity[(int)pid];
                else
                    e = Entity.Null;

                *(Entity*)(dst + dstOff) = e;
                dstOff += 8;
                srcOff += 4;
            }
            int remaining = dataStride - srcOff;
            for (int b = 0; b < remaining; b++)
                dst[dstOff + b] = src[srcOff + b];
        }
    }
}
