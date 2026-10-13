using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    public struct ModuleBackup
    {
        public SaveTypeModule Module;
        public NativeArray<byte> Values;
        public NativeArray<byte> Presence;
        public NativeArray<byte> Enabled;
        public NativeArray<int> BufferCounts;
        public int Count;
        public NativeArray<Entity> AnchorEntities;
        public NativeArray<byte> AnchorValues;
        public int AnchorCount;
    }
}
