using ECSave;
using ECSave.Format;
using ECSave.Internal;
using Game.Domain;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Game.MinimalE2E
{
    internal sealed class PlayerIdModule : IdentityModule
    {
        public const string TypeFullName = "Game.Domain.PlayerId";

        private static readonly byte[] s_schemaHashInput = BuildInput();
        private static readonly ulong s_schemaHash = SchemaHashCore.Hash(s_schemaHashInput);
        private static readonly string[] s_fieldNames = { "Value" };

        public override ulong SchemaHash => s_schemaHash;
        public override string FullTypeName => TypeFullName;
        public unsafe override int KeyStride => sizeof(PlayerId);
        public override byte[] SchemaHashInput => s_schemaHashInput;
        public override string[] FieldNames => s_fieldNames;

        public override EntityQuery BuildQuery(EntityManager em)
            => ModuleQueryCache.GetOrCreate(em, this, () =>
                em.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<PlayerId>() },
                    Options = EntityQueryOptions.IgnoreComponentEnabledState
                            | EntityQueryOptions.IncludeDisabledEntities,
                }));

        public override int ComputeBodySize(
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf)
        {
            var q = BuildQuery(em);
            using var entities = q.ToEntityArray(Allocator.TempJob);
            int count = 0;
            for (int i = 0; i < entities.Length; i++)
                if (idOf.ContainsKey(entities[i])) count++;
            return CommonSectionWriter.ComputeFixedBodySize(
                count, KeyStride, hasEnabledBits: false);
        }

        public override unsafe void WriteSectionBody(
            ref FormatWriter writer,
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf)
        {
            var q = BuildQuery(em);
            using var entities = q.ToEntityArray(Allocator.TempJob);
            using var values = q.ToComponentDataArray<PlayerId>(Allocator.TempJob);

            int count = 0;
            for (int i = 0; i < entities.Length; i++)
                if (idOf.ContainsKey(entities[i])) count++;

            writer.WriteU32((uint)count);
            writer.WriteU32(ECSaveFormatConstants.SectionFlags_FixedLength);
            writer.WriteU32((uint)KeyStride);

            if (count > 0)
            {
                using var indices = new NativeArray<uint>(count, Allocator.TempJob);
                uint* idst = (uint*)indices.GetUnsafePtr();
                int j = 0;
                for (int i = 0; i < entities.Length; i++)
                    if (idOf.ContainsKey(entities[i]))
                        idst[j++] = (uint)idOf[entities[i]];
                writer.WriteBytes((byte*)idst, count * 4);
            }

            if (KeyStride > 0 && count > 0)
            {
                using var records = new NativeArray<byte>(count * KeyStride, Allocator.TempJob);
                byte* rptr = (byte*)records.GetUnsafePtr();
                byte* vsrc = (byte*)values.GetUnsafeReadOnlyPtr();
                int j = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!idOf.ContainsKey(entities[i])) continue;
                    for (int b = 0; b < KeyStride; b++)
                        rptr[j * KeyStride + b] = vsrc[i * KeyStride + b];
                    j++;
                }
                writer.WriteBytes(rptr, count * KeyStride);
            }
        }

        public override SaveLoadErrorCode BuildWorldIndex(
            EntityManager em,
            EntityQuery query,
            ref IdentityIndex index,
            uint typeIndex,
            NativeParallelHashSet<Entity> scopeFilter)
        {
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var values = query.ToComponentDataArray<PlayerId>(Allocator.TempJob);

            bool hasFilter = scopeFilter.IsCreated;

            unsafe
            {
                byte* src = (byte*)values.GetUnsafeReadOnlyPtr();
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    if (hasFilter && !scopeFilter.Contains(entity)) continue;

                    byte* key = src + i * KeyStride;
                    if (index.HasWorldConflict(typeIndex, key, KeyStride))
                        return SaveLoadErrorCode.ValidationFailed;
                    index.AddWorldEntry(typeIndex, key, KeyStride, entity);
                }
            }
            return SaveLoadErrorCode.None;
        }

        public override unsafe SaveLoadErrorCode BuildSaveIndex(
            ByteView sectionBody,
            ref IdentityIndex index,
            uint typeIndex)
        {
            var v = CommonSectionReader.Read(sectionBody);
            int count = (int)v.Header.Count;

            for (int i = 0; i < count; i++)
            {
                uint pid = v.EntityIndex[i];
                byte* src = v.Records + i * KeyStride;
                if (index.HasSaveConflict(typeIndex, src, KeyStride))
                    return SaveLoadErrorCode.ValidationFailed;
                index.AddSaveEntry(typeIndex, src, KeyStride, (int)pid);
            }
            return SaveLoadErrorCode.None;
        }

        public override unsafe void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            var v = CommonSectionReader.Read(sectionBody);
            int count = (int)v.Header.Count;

            for (int i = 0; i < count; i++)
            {
                uint pid = v.EntityIndex[i];
                if (pid >= persistentToEntity.Length) continue;

                Entity target = persistentToEntity[(int)pid];
                if (target == Entity.Null) continue;

                PlayerId value = default;
                byte* valPtr = (byte*)&value;
                byte* srcPtr = v.Records + i * KeyStride;
                for (int b = 0; b < KeyStride; b++)
                    valPtr[b] = srcPtr[b];

                if (em.HasComponent<PlayerId>(target))
                    em.SetComponentData(target, value);
                else
                    em.AddComponentData(target, value);
            }
        }

        private unsafe static byte[] BuildInput()
        {
            var buf = new byte[2 + 16];
            buf[0] = (byte)(sizeof(PlayerId) & 0xFF);
            buf[1] = (byte)(sizeof(PlayerId) >> 8);
            buf[2] = (byte)SchemaHashKind.Int32;
            buf[3] = 4;
            buf[4] = 0;
            return buf;
        }
    }
}
