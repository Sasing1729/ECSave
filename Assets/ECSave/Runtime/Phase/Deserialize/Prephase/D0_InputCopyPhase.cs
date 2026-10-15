using ECSave.Format;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    internal static unsafe class D0_InputCopyPhase
    {
        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx,
            byte[] data,
            SaveDefinition definition,
            World world,
            CancellationToken ct)
        {
            if (data == null || data.Length == 0)
                return SaveLoadErrorCode.CorruptedData;

            if (ct.IsCancellationRequested)
                return SaveLoadErrorCode.Cancelled;

            bool compressed = SaveCompression.IsCompressed(data);
            int rawLength = data.Length;
            if (compressed)
            {
                var err = SaveCompression.TryReadRawLength(data, out rawLength);
                if (err != SaveLoadErrorCode.None) return err;
            }

            ctx.Definition = definition;
            ctx.World = world;

            ctx.NativeData = new NativeArray<byte>(
                rawLength, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            byte* basePtr = (byte*)ctx.NativeData.GetUnsafePtr();

            if (compressed)
            {
                if (!SaveCompression.Decompress(data, basePtr, rawLength))
                    return SaveLoadErrorCode.CorruptedData;
            }
            else
            {
                NativeArray<byte>.Copy(data, ctx.NativeData, data.Length);
            }

            ctx.View = new ByteView(basePtr, rawLength);

            return SaveLoadErrorCode.None;
        }
    }
}