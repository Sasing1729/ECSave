using Unity.Collections.LowLevel.Unsafe;

namespace ECSave.Internal
{
    internal static class ModuleBytes
    {
        public static unsafe T Read<T>(byte* src, int length) where T : unmanaged
        {
            T value = default;
            UnsafeUtility.MemCpy(&value, src, length);
            return value;
        }

        public static unsafe void Write<T>(T value, byte* dst) where T : unmanaged
            => UnsafeUtility.MemCpy(dst, &value, UnsafeUtility.SizeOf<T>());
    }
}