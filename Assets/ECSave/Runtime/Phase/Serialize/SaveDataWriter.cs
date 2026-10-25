using ECSave.Format;
using ECSave.Internal.SubScene;
using ECSave.Phase.Serialize;
using System;
using System.Text;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace ECSave.Internal
{
    /// <summary>
    /// SaveData のバイト列書き込み。
    /// 元の <c>SerializeOperation.WriteSaveData</c> と完全に同一のロジック。
    /// </summary>
    internal static unsafe class SaveDataWriter
    {
        public static void Write(
            byte* p, in SaveLayout layout,
            SaveDefinition def,
            World world,
            NativeList<Entity> entities,
            NativeParallelHashMap<Entity, int> idOf,
            SnapshotBuffer[] buffers,
            EntityManager em,
            CancellationToken ct,
            S2_5_SubSceneCapturePhase.Result subSceneResult,
            S2_6_TombstoneCapturePhase.Result tombstoneResult,
            S2_7_PrefabIdentityCapturePhase.Result prefabResult)
        {
            int n = def.Modules.Count;
            int m = def.Identities != null ? def.Identities.Count : 0;

            var header = new SaveHeader
            {
                Magic = ECSaveFormatConstants.Magic,
                FormatVersion = ECSaveFormatConstants.CurrentFormatVersion,
                HeaderFlags = 0,
                HeaderSize = (uint)(layout.HeaderFixedSize + layout.WorldIdLength),
                GameDataVersion = def.GameDataVersion,
                LogicalEntityCount = entities.Length,
                SectionCount = layout.TotalSections,
                SectionTableOffset = (ulong)layout.SectionTableOffset,
                TotalLength = (ulong)layout.TotalSize,
                NameHeapLength = (uint)layout.NameHeapLength,
                WorldIdLength = (ushort)layout.WorldIdLength,
                Reserved = 0,
            };

            var writer = new FormatWriter(p, layout.TotalSize);
            writer.WriteHeader(header);

            var worldIdBytes = Encoding.UTF8.GetBytes(GetEffectiveWorldId(def, world));
            if (worldIdBytes.Length > 0)
            {
                fixed (byte* wid = worldIdBytes)
                    writer.WriteBytes(wid, worldIdBytes.Length);
            }

            writer.Seek(layout.SectionTableOffset);
            for (int i = 0; i < n; i++)
            {
                var mod = def.Modules[i];
                var entry = new SectionEntry
                {
                    SectionId = (uint)i,
                    Kind = mod.SectionKind,
                    Flags = ECSaveFormatConstants.SectionEntryFlags_HasTypeDescriptor,
                    NameLength = (ushort)layout.NameLengths[i],
                    NameHash = SchemaHashCore.HashUtf8(mod.FullTypeName),
                    SchemaHash = mod.SchemaHash,
                    Offset = (ulong)layout.SectionOffsets[i],
                    Length = (ulong)layout.SectionLengths[i],
                    NameOffset = (uint)layout.NameOffsets[i],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }
            for (int i = 0; i < m; i++)
            {
                int idx = n + i;
                var im = def.Identities[i];
                var entry = new SectionEntry
                {
                    SectionId = (uint)idx,
                    Kind = SectionKind.Identity,
                    Flags = ECSaveFormatConstants.SectionEntryFlags_HasTypeDescriptor,
                    NameLength = (ushort)layout.NameLengths[idx],
                    NameHash = SchemaHashCore.HashUtf8(im.FullTypeName),
                    SchemaHash = im.SchemaHash,
                    Offset = (ulong)layout.SectionOffsets[idx],
                    Length = (ulong)layout.SectionLengths[idx],
                    NameOffset = (uint)layout.NameOffsets[idx],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }
            if (layout.HasSubScene)
            {
                int idx = n + m;
                var entry = new SectionEntry
                {
                    SectionId = (uint)idx,
                    Kind = SectionKind.SubSceneState,
                    Flags = 0,
                    NameLength = (ushort)layout.NameLengths[idx],
                    NameHash = SchemaHashCore.HashUtf8(SubSceneStateSection.SectionName),
                    SchemaHash = 0,
                    Offset = (ulong)layout.SectionOffsets[idx],
                    Length = (ulong)layout.SectionLengths[idx],
                    NameOffset = (uint)layout.NameOffsets[idx],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }
            for (int i = 0; i < layout.TombstoneCount; i++)
            {
                int idx = n + m + (layout.HasSubScene ? 1 : 0) + i;
                var cap = tombstoneResult.Captured[i];
                var entry = new SectionEntry
                {
                    SectionId = (uint)idx,
                    Kind = SectionKind.SubSceneTombstone,
                    Flags = 0,
                    NameLength = (ushort)layout.NameLengths[idx],
                    NameHash = SubSceneTombstoneSection.NameHashFor(cap.Handler.FullTypeName),
                    SchemaHash = 0,
                    Offset = (ulong)layout.SectionOffsets[idx],
                    Length = (ulong)layout.SectionLengths[idx],
                    NameOffset = (uint)layout.NameOffsets[idx],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }
            if (layout.HasPrefabIdentity)
            {
                int idx = n + m + (layout.HasSubScene ? 1 : 0) + layout.TombstoneCount;
                var entry = new SectionEntry
                {
                    SectionId = (uint)idx,
                    Kind = SectionKind.PrefabIdentity,
                    Flags = 0,
                    NameLength = (ushort)layout.NameLengths[idx],
                    NameHash = SchemaHashCore.HashUtf8(PrefabIdentitySection.SectionName),
                    SchemaHash = 0,
                    Offset = (ulong)layout.SectionOffsets[idx],
                    Length = (ulong)layout.SectionLengths[idx],
                    NameOffset = (uint)layout.NameOffsets[idx],
                    Reserved = 0,
                };
                writer.WriteSectionEntry(entry);
            }

            writer.Seek(layout.NameHeapOffset);
            for (int i = 0; i < n; i++)
            {
                var nameBytes = Encoding.UTF8.GetBytes(def.Modules[i].FullTypeName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            for (int i = 0; i < m; i++)
            {
                var nameBytes = Encoding.UTF8.GetBytes(def.Identities[i].FullTypeName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            if (layout.HasSubScene)
            {
                var nameBytes = Encoding.UTF8.GetBytes(SubSceneStateSection.SectionName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            for (int i = 0; i < layout.TombstoneCount; i++)
            {
                var nameBytes = Encoding.UTF8.GetBytes(tombstoneResult.Captured[i].SectionName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            if (layout.HasPrefabIdentity)
            {
                var nameBytes = Encoding.UTF8.GetBytes(PrefabIdentitySection.SectionName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                writer.Seek(layout.SectionOffsets[i]);
                var mod = def.Modules[i];

                var input = mod.SchemaHashInput;
                fixed (byte* inputPtr = input)
                {
                    TypeDescriptorBlockWriter.Write(
                        ref writer,
                        entryCount: (ushort)mod.FieldNames.Length,
                        rawEntryCount: 0,
                        schemaHashInput: inputPtr,
                        schemaHashInputLength: input.Length,
                        fieldNames: mod.FieldNames);
                }

                mod.WriteSectionBody(ref writer, buffers[i], idOf);
            }
            for (int i = 0; i < m; i++)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                int idx = n + i;
                writer.Seek(layout.SectionOffsets[idx]);
                var im = def.Identities[i];

                var input = im.SchemaHashInput;
                fixed (byte* inputPtr = input)
                {
                    TypeDescriptorBlockWriter.Write(
                        ref writer,
                        entryCount: (ushort)im.FieldNames.Length,
                        rawEntryCount: 0,
                        schemaHashInput: inputPtr,
                        schemaHashInputLength: input.Length,
                        fieldNames: im.FieldNames);
                }

                im.WriteSectionBody(ref writer, em, idOf);
            }
            if (layout.HasSubScene)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                int idx = n + m;
                writer.Seek(layout.SectionOffsets[idx]);
                SubSceneStateSection.Write(ref writer, subSceneResult.Scenes);
            }
            for (int i = 0; i < layout.TombstoneCount; i++)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                int idx = n + m + (layout.HasSubScene ? 1 : 0) + i;
                writer.Seek(layout.SectionOffsets[idx]);
                var cap = tombstoneResult.Captured[i];
                SubSceneTombstoneSection.Write(ref writer, cap.Keys, cap.Handler.KeyStride);
            }
            if (layout.HasPrefabIdentity)
            {
                if (ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);

                int idx = n + m + (layout.HasSubScene ? 1 : 0) + layout.TombstoneCount;
                writer.Seek(layout.SectionOffsets[idx]);
                PrefabIdentitySection.Write(ref writer, prefabResult.Records);
            }

            int checksumEnd = layout.FooterOffset;
            ulong checksum = ChecksumCalculator.Compute(new ByteView(p, checksumEnd));

            writer.Seek(layout.FooterOffset);
            writer.WriteFooter(checksum);
        }

        /// <summary>
        /// Serialize 時にヘッダへ書く WorldId を返す。
        /// <see cref="SaveDefinition.WorldId"/> が空の場合、World 名を暗黙の既定値として使う。
        /// </summary>
        private static string GetEffectiveWorldId(SaveDefinition def, World world)
            => string.IsNullOrEmpty(def.WorldId) ? world.Name : def.WorldId;
    }
}
