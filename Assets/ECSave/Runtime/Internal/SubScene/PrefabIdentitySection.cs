using System;
using System.Collections.Generic;
using ECSave.Format;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal.SubScene
{
    internal static class PrefabIdentityLayout
    {
        public const int HeaderSize = 8;
        public const int RecordSize = 24;

        public const int EntityIndexOffset = 0;
        public const int PrefabGuidOffset = 8;
        public const int SchemaHashOffset = 16;

        public static int ComputeBodySize(int count)
            => HeaderSize + count * RecordSize;
    }

    internal readonly unsafe struct PrefabIdentitySectionView
    {
        public readonly uint Count;
        public readonly byte* Records;

        public PrefabIdentitySectionView(uint count, byte* records)
        {
            Count = count;
            Records = records;
        }
    }

    internal static unsafe class PrefabIdentitySection
    {
        public const string SectionName = "ECSave.PrefabIdentity";

        public static PrefabIdentitySectionView Read(byte* pointer, int length)
        {
            if (pointer == null || length < PrefabIdentityLayout.HeaderSize)
                throw new FormatException(
                    FormatError.CorruptedData,
                    $"PrefabIdentity section too short: length={length}, header={PrefabIdentityLayout.HeaderSize}");

            uint count = *(uint*)pointer;

            long required = PrefabIdentityLayout.HeaderSize
                          + (long)count * PrefabIdentityLayout.RecordSize;
            if (required > length)
                throw new FormatException(
                    FormatError.CorruptedData,
                    $"PrefabIdentity section truncated: count={count}, required={required}, length={length}");

            return new PrefabIdentitySectionView(
                count, pointer + PrefabIdentityLayout.HeaderSize);
        }

        public static void Write(ref FormatWriter w, IReadOnlyList<PrefabIdentityRecord> records)
        {
            int count = records?.Count ?? 0;
            w.WriteU32((uint)count);
            w.WriteU32(0);

            if (records == null) return;

            for (int i = 0; i < count; i++)
            {
                var r = records[i];
                w.WriteU32(r.EntityIndex);
                w.WriteU32(0);
                w.WriteU64(r.PrefabGuid);
                w.WriteU64(r.SchemaHash);
            }
        }

        public static uint GetEntityIndex(in PrefabIdentitySectionView v, int i)
            => *(uint*)(v.Records + i * PrefabIdentityLayout.RecordSize + PrefabIdentityLayout.EntityIndexOffset);

        public static ulong GetPrefabGuid(in PrefabIdentitySectionView v, int i)
            => *(ulong*)(v.Records + i * PrefabIdentityLayout.RecordSize + PrefabIdentityLayout.PrefabGuidOffset);

        public static ulong GetSchemaHash(in PrefabIdentitySectionView v, int i)
            => *(ulong*)(v.Records + i * PrefabIdentityLayout.RecordSize + PrefabIdentityLayout.SchemaHashOffset);
    }

    internal readonly struct PrefabIdentityRecord
    {
        public readonly uint EntityIndex;
        public readonly ulong PrefabGuid;
        public readonly ulong SchemaHash;

        public PrefabIdentityRecord(uint entityIndex, ulong prefabGuid, ulong schemaHash)
        {
            EntityIndex = entityIndex;
            PrefabGuid = prefabGuid;
            SchemaHash = schemaHash;
        }
    }
}
