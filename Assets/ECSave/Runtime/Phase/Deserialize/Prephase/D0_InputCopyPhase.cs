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

            ctx.Definition = definition;
            ctx.World = world;

            ctx.NativeData = new NativeArray<byte>(data.Length, Allocator.Persistent);
            NativeArray<byte>.Copy(data, ctx.NativeData, data.Length);
            ctx.View = new ByteView((byte*)ctx.NativeData.GetUnsafePtr(), data.Length);

            return SaveLoadErrorCode.None;
        }
    }
}
