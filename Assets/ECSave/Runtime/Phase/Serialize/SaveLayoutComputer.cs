using ECSave.Format;
using ECSave.Internal.SubScene;
using ECSave.Phase.Serialize;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal
{
    /// <summary>
    /// Serialize 時の SaveData レイアウト。
    /// 元の <c>SerializeOperation.Layout</c> と完全に同一のフィールド構成。
    /// </summary>
    internal struct SaveLayout
    {
        public int HeaderFixedSize;
        public int WorldIdLength;
        public int SectionTableOffset;
        public int NameHeapOffset;
        public int NameHeapLength;
        public int SectionsStart;
        public int[] SectionOffsets;
        public int[] SectionLengths;
        public int[] NameOffsets;
        public int[] NameLengths;
        public int FooterOffset;
        public int TotalSize;
        public int TotalSections;
        public bool HasSubScene;
        public int TombstoneCount;
        public bool HasPrefabIdentity;
    }

    /// <summary>
    /// SaveData のレイアウト計算。
    /// 元の <c>SerializeOperation.ComputeLayout</c> と完全に同一のロジック。
    /// </summary>
    internal static class SaveLayoutComputer
    {
        public static SaveLayout Compute(
            SaveDefinition def,
            World world,
            int logicalEntityCount,
            SnapshotBuffer[] buffers,
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf,
            S2_5_SubSceneCapturePhase.Result subSceneResult,
            S2_6_TombstoneCapturePhase.Result tombstoneResult,
            S2_7_PrefabIdentityCapturePhase.Result prefabResult)
        {
            int n = def.Modules.Count;
            int m = def.Identities != null ? def.Identities.Count : 0;
            bool hasSubScene = subSceneResult.HasSection;
            int tombstoneCount = tombstoneResult.Captured.Count;
            bool hasPrefabIdentity = prefabResult.HasSection;
            int total = n + m
                + (hasSubScene ? 1 : 0)
                + tombstoneCount
                + (hasPrefabIdentity ? 1 : 0);

            var layout = new SaveLayout
            {
                HeaderFixedSize = ECSaveFormatConstants.HeaderFixedSize,
                WorldIdLength = Encoding.UTF8.GetByteCount(GetEffectiveWorldId(def, world)),
                SectionOffsets = new int[total],
                SectionLengths = new int[total],
                NameOffsets = new int[total],
                NameLengths = new int[total],
                TotalSections = total,
                HasSubScene = hasSubScene,
                TombstoneCount = tombstoneCount,
                HasPrefabIdentity = hasPrefabIdentity,
            };

            int headerEnd = layout.HeaderFixedSize + layout.WorldIdLength;
            layout.SectionTableOffset = AlignUp(headerEnd, 8);
            layout.NameHeapOffset = layout.SectionTableOffset + total * ECSaveFormatConstants.SectionEntrySize;

            int nameHeapCursor = 0;
            for (int i = 0; i < n; i++)
            {
                layout.NameOffsets[i] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(def.Modules[i].FullTypeName);
                layout.NameLengths[i] = len;
                nameHeapCursor += len;
            }
            for (int i = 0; i < m; i++)
            {
                int idx = n + i;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(def.Identities[i].FullTypeName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            if (hasSubScene)
            {
                int idx = n + m;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(SubSceneStateSection.SectionName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            for (int i = 0; i < tombstoneCount; i++)
            {
                int idx = n + m + (hasSubScene ? 1 : 0) + i;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(tombstoneResult.Captured[i].SectionName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            if (hasPrefabIdentity)
            {
                int idx = n + m + (hasSubScene ? 1 : 0) + tombstoneCount;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(PrefabIdentitySection.SectionName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            layout.NameHeapLength = nameHeapCursor;

            layout.SectionsStart = AlignUp(layout.NameHeapOffset + layout.NameHeapLength, 8);

            int cursor = layout.SectionsStart;
            for (int i = 0; i < n; i++)
            {
                layout.SectionOffsets[i] = cursor;

                var mod = def.Modules[i];
                int typeDescSize = ComputeTypeDescSize(
                    mod.SchemaHashInput.Length, mod.FieldNames);
                int bodySize = mod.ComputeBodySize(buffers[i]);

                layout.SectionLengths[i] = typeDescSize + bodySize;
                cursor += layout.SectionLengths[i];
                cursor = AlignUp(cursor, 8);
            }
            for (int i = 0; i < m; i++)
            {
                int idx = n + i;
                layout.SectionOffsets[idx] = cursor;

                var im = def.Identities[i];
                int typeDescSize = ComputeTypeDescSize(
                    im.SchemaHashInput.Length, im.FieldNames);
                int bodySize = im.ComputeBodySize(em, idOf);

                layout.SectionLengths[idx] = typeDescSize + bodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }
            if (hasSubScene)
            {
                int idx = n + m;
                layout.SectionOffsets[idx] = cursor;
                layout.SectionLengths[idx] = subSceneResult.SectionBodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }
            for (int i = 0; i < tombstoneCount; i++)
            {
                int idx = n + m + (hasSubScene ? 1 : 0) + i;
                layout.SectionOffsets[idx] = cursor;
                layout.SectionLengths[idx] = tombstoneResult.Captured[i].SectionBodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }
            if (hasPrefabIdentity)
            {
                int idx = n + m + (hasSubScene ? 1 : 0) + tombstoneCount;
                layout.SectionOffsets[idx] = cursor;
                layout.SectionLengths[idx] = prefabResult.SectionBodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }

            // P-4: cursor は int 加算で蓄積されるため、2GB 近傍で折り返す可能性がある。
            if (cursor < 0 || cursor > int.MaxValue - ECSaveFormatConstants.FooterSize)
            {
                throw new FormatException(FormatError.ValidationFailed,
                    $"save layout out of int range: cursor={cursor}");
            }

            layout.FooterOffset = cursor;
            layout.TotalSize = layout.FooterOffset + ECSaveFormatConstants.FooterSize;
            return layout;
        }

        /// <summary>
        /// Serialize 時にヘッダへ書く WorldId を返す。
        /// <see cref="SaveDefinition.WorldId"/> が空の場合、World 名を暗黙の既定値として使う。
        /// </summary>
        private static string GetEffectiveWorldId(SaveDefinition def, World world)
            => string.IsNullOrEmpty(def.WorldId) ? world.Name : def.WorldId;

        private static int ComputeTypeDescSize(int inputLen, string[] fieldNames)
        {
            int utf8Total = 0;
            for (int i = 0; i < fieldNames.Length; i++)
                utf8Total += Encoding.UTF8.GetByteCount(fieldNames[i]);
            return TypeDescriptorBlockWriter.ComputeSize(inputLen, fieldNames.Length, utf8Total);
        }

        private static int AlignUp(int value, int alignment)
        {
            int rem = value % alignment;
            return rem == 0 ? value : value + (alignment - rem);
        }
    }
}
