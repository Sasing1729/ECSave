using ECSave.Format;
using Unity.Collections;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;

namespace ECSave.Internal
{
    internal unsafe delegate void WriteValueDelegate(
        Entity entity, EntityManager em, byte* value);

    internal unsafe delegate void ApplyEnabledDelegate(
        Entity entity, EntityManager em, bool enabled);

    internal unsafe delegate void ApplyBufferRecordDelegate(
        Entity target, EntityManager em, byte* src, int elementCount);

    internal static class ModuleShared
    {
        public static readonly int[] EmptyInts = System.Array.Empty<int>();

        public static void RemoveComponent<T>(Entity entity, EntityManager em)
            where T : unmanaged
        {
            if (em.HasComponent<T>(entity))
                em.RemoveComponent<T>(entity);
        }

        public static unsafe void ApplyFixedSection(
            ByteView sectionBody,
            int dataStride,
            int rawStride,
            int[] refs,
            bool moduleHasEnabledBits,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            WriteValueDelegate writeValue,
            ApplyEnabledDelegate applyEnabled)
        {
            var v = CommonSectionReader.Read(sectionBody);
            int count = (int)v.Header.Count;

            if ((int)v.Header.Stride != dataStride)
                throw new FormatException(FormatError.SchemaMismatch,
                    $"stride mismatch: section={v.Header.Stride}, expected={dataStride}");

            bool hasEnabled = v.Header.HasEnabledBits && moduleHasEnabledBits;
            if (v.Header.HasEnabledBits && !moduleHasEnabledBits)
                throw new FormatException(FormatError.SchemaMismatch,
                    "section has enabled bits but module does not support them");

            byte[] expanded = rawStride > 0 ? new byte[rawStride] : null;
            fixed (byte* eptr = expanded)
            {
                for (int i = 0; i < count; i++)
                {
                    uint pid = v.EntityIndex[i];
                    if (pid >= persistentToEntity.Length)
                        throw new FormatException(FormatError.ValidationFailed,
                            "persistent id out of range");

                    Entity target = persistentToEntity[(int)pid];
                    if (target == Entity.Null)
                    {
                        target = em.CreateEntity();
                        persistentToEntity[(int)pid] = target;
                    }

                    byte* record = dataStride > 0 ? v.Records + i * dataStride : null;
                    if (refs.Length == 0)
                    {
                        writeValue(target, em, record);
                    }
                    else
                    {
                        EntityRefCodec.ExpandRecord(
                            record, dataStride, refs, rawStride, persistentToEntity, eptr);
                        writeValue(target, em, eptr);
                    }

                    if (hasEnabled)
                        applyEnabled(target, em, SectionBodyHelper.UnpackEnabled(v.EnabledBits, i));
                }
            }
        }

        public static unsafe void ApplyVariableSection(
            ByteView sectionBody,
            int dataStride,
            bool moduleHasEnabledBits,
            NativeArray<Entity> persistentToEntity,
            EntityManager em,
            ApplyBufferRecordDelegate applyRecord,
            ApplyEnabledDelegate applyEnabled)
        {
            var v = CommonSectionReader.Read(sectionBody);

            if (v.Header.IsFixedLength)
                throw new FormatException(FormatError.SchemaMismatch,
                    "section is fixed-length but module expects variable-length");

            if ((int)v.Header.Stride != dataStride)
                throw new FormatException(FormatError.SchemaMismatch,
                    $"stride mismatch: section={v.Header.Stride}, expected={dataStride}");

            bool hasEnabled = v.Header.HasEnabledBits && moduleHasEnabledBits;
            if (v.Header.HasEnabledBits && !moduleHasEnabledBits)
                throw new FormatException(FormatError.SchemaMismatch,
                    "section has enabled bits but module does not support them");

            int count = (int)v.Header.Count;

            for (int i = 0; i < count; i++)
            {
                uint pid = v.EntityIndex[i];
                if (pid >= persistentToEntity.Length)
                    throw new FormatException(FormatError.ValidationFailed,
                        "persistent id out of range");

                Entity target = persistentToEntity[(int)pid];
                if (target == Entity.Null)
                {
                    target = em.CreateEntity();
                    persistentToEntity[(int)pid] = target;
                }

                uint offsetStart = v.RecordOffset[i];
                uint offsetEnd = v.RecordOffset[i + 1];
                int elementCount = dataStride > 0
                    ? (int)((offsetEnd - offsetStart) / (uint)dataStride)
                    : 0;

                applyRecord(target, em, v.Records + offsetStart, elementCount);

                if (hasEnabled)
                    applyEnabled(target, em, SectionBodyHelper.UnpackEnabled(v.EnabledBits, i));
            }
        }
    }
}
