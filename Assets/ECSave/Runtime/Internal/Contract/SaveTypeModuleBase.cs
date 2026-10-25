using ECSave.Format;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    public abstract class SaveTypeModuleBase<T> : SaveTypeModule
        where T : unmanaged
    {
        public override int RawRecordStride => UnsafeUtility.SizeOf<T>();
        public override int[] EntityRefOffsets => ModuleShared.EmptyInts;
        public override int RecordStride => UnsafeUtility.SizeOf<T>() - 4 * EntityRefOffsets.Length;
        public override bool HasEnabledBits => false;
        public override SectionKind SectionKind => SectionKind.Component;

        protected virtual ComponentType QueryType => ComponentType.ReadOnly<T>();

        public override EntityQuery BuildQuery(EntityManager em)
            => ModuleQueryCache.GetOrCreate(em, this, () =>
                em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { QueryType },
                    // QueryOptions は削除。値は inline で与える。
                    // - protected virtual で override 元ゼロ
                    // - 生成コード (ModuleEmitter) は BuildQuery を完全 override するため無関係
                    // - 「API を減らす」方針に沿う
                    Options = EntityQueryOptions.IgnoreComponentEnabledState
                            | EntityQueryOptions.IncludeDisabledEntities,
                }));

        public override void RemoveFromEntity(Entity entity, EntityManager em)
            => ModuleShared.RemoveComponent<T>(entity, em);

        protected bool WritesEnabledBits(in SnapshotBuffer buffer)
            => HasEnabledBits && buffer.HasEnabledStates;
    }

    public abstract class SaveTypeModuleFixed<T> : SaveTypeModuleBase<T>
        where T : unmanaged
    {
        public override int ComputeBodySize(in SnapshotBuffer buffer)
            => CommonSectionWriter.ComputeFixedBodySize(
                buffer.Count, RecordStride,
                hasEnabledBits: WritesEnabledBits(in buffer));

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            in SnapshotBuffer buffer,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int count = buffer.Count;
            bool hasEnabled = WritesEnabledBits(in buffer);
            int rawStride = RawRecordStride;
            int dataStride = RecordStride;
            var refs = EntityRefOffsets;

            SectionBodyHelper.WriteFixedHeader(ref writer, count, dataStride, hasEnabled);
            SectionBodyHelper.WriteEntityIndices(ref writer, in buffer, idOf);

            if (hasEnabled)
                SectionBodyHelper.WriteEnabledBits(ref writer, in buffer, count);

            if (dataStride <= 0 || count == 0) return;

            if (refs.Length == 0)
            {
                byte* src = (byte*)buffer.Values.GetUnsafeReadOnlyPtr();
                writer.WriteBytes(src, count * rawStride);
            }
            else
            {
                for (int i = 0; i < count; i++)
                    EntityRefCodec.WriteRecordWithRefs(
                        ref writer, buffer.GetValuePtr(i), refs, rawStride, idOf);
            }
        }

        public unsafe override void RestoreSection(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            ModuleShared.ApplyFixedSection(
                sectionBody, RecordStride, RawRecordStride, EntityRefOffsets, HasEnabledBits,
                persistentToEntity, em, WriteValue, ApplyEnabled);
        }

        protected abstract unsafe void WriteValue(Entity entity, EntityManager em, byte* value);
    }
}
