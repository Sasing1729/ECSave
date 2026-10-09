using ECSave.Format;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    public abstract class IdentityModule
    {
        public int RegistrationId { get; internal set; }
        public abstract ulong SchemaHash { get; }
        public abstract string FullTypeName { get; }
        public abstract int KeyStride { get; }
        public abstract byte[] SchemaHashInput { get; }
        public abstract string[] FieldNames { get; }

        public abstract EntityQuery BuildQuery(EntityManager em);

        public abstract int ComputeBodySize(
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf);

        public abstract void WriteSectionBody(
            ref FormatWriter writer,
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf);

        public abstract SaveLoadErrorCode BuildWorldIndex(
            EntityManager em,
            EntityQuery query,
            ref IdentityIndex index,
            uint typeIndex);

        public abstract SaveLoadErrorCode BuildSaveIndex(
            ByteView sectionBody,
            ref IdentityIndex index,
            uint typeIndex);

        public abstract void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em);
    }
}
