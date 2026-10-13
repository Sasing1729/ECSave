using System;
using System.Text;
using System.Threading;
using ECSave.Format;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal sealed unsafe class SerializeOperation
    {
        private readonly SaveDefinition _def;
        private readonly World _world;

        public SerializeOperation(SaveDefinition def, World world)
        {
            _def = def;
            _world = world;
        }

        public SaveResult Execute(CancellationToken ct)
        {
            var em = _world.EntityManager;
            int n = _def.Modules.Count;
            int m = _def.Identities != null ? _def.Identities.Count : 0;

            var buffers = new SnapshotBuffer[n];

            try
            {
                for (int i = 0; i < n; i++)
                {
                    if (ct.IsCancellationRequested)
                        return SaveResult.Failure(SaveLoadErrorCode.Cancelled);

                    var mod = _def.Modules[i];
                    buffers[i] = new SnapshotBuffer(256, mod.RawRecordStride, Allocator.TempJob);

                    if (mod.HasEnabledBits)
                        buffers[i].InitEnabledStates(Allocator.TempJob);

                    var query = mod.BuildQuery(em);
                    mod.Capture(query, em, ref buffers[i]);
                }

                int totalUpperBound = 0;
                for (int i = 0; i < n; i++)
                {
                    if (!_def.Modules[i].ContributesToIdOf) continue;
                    totalUpperBound += buffers[i].Count;
                }

                int initialCap = totalUpperBound > 0 ? totalUpperBound : 16;

                var idOf = new NativeParallelHashMap<Entity, int>(initialCap, Allocator.TempJob);
                var entityList = new NativeList<Entity>(initialCap, Allocator.TempJob);

                try
                {
                    for (int i = 0; i < n; i++)
                    {
                        if (!_def.Modules[i].ContributesToIdOf) continue;
                        var entities = buffers[i].Entities;
                        for (int j = 0; j < entities.Length; j++)
                        {
                            var e = entities[j];
                            if (idOf.TryAdd(e, entityList.Length))
                                entityList.Add(e);
                        }
                    }

                    for (int i = 0; i < n; i++)
                    {
                        if (_def.Modules[i].ContributesToIdOf) continue;
                        _def.Modules[i].FilterAfterIdOf(ref buffers[i], idOf);
                    }

                    AddSelections(em, idOf, entityList, ct);

                    int logicalEntityCount = entityList.Length;

                    var layout = ComputeLayout(logicalEntityCount, buffers, em, idOf);

                    byte[] result = new byte[layout.TotalSize];
                    fixed (byte* p = result)
                    {
                        WriteSaveData(p, layout, entityList, idOf, buffers, em, ct);
                    }

                    return SaveResult.Success(result);
                }
                finally
                {
                    if (idOf.IsCreated) idOf.Dispose();
                    if (entityList.IsCreated) entityList.Dispose();
                }
            }
            finally
            {
                for (int i = 0; i < n; i++)
                    if (buffers[i].IsCreated) buffers[i].Dispose();
            }
        }

        private void AddSelections(
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf,
            NativeList<Entity> entityList,
            CancellationToken ct)
        {
            var selections = _def.Selections;
            if (selections == null || selections.Count == 0) return;

            var selected = new NativeParallelHashSet<Entity>(64, Allocator.TempJob);
            try
            {
                for (int i = 0; i < selections.Count; i++)
                {
                    if (ct.IsCancellationRequested) return;

                    var sel = selections[i];
                    var q = sel.BuildQuery(em);
                    sel.ExtractSelected(q, em, selected);
                }

                foreach (var e in selected)
                {
                    if (e == Entity.Null) continue;
                    if (!em.Exists(e)) continue;

                    if (idOf.TryAdd(e, entityList.Length))
                        entityList.Add(e);
                }
            }
            finally
            {
                if (selected.IsCreated) selected.Dispose();
            }
        }

        private struct Layout
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
        }

        private Layout ComputeLayout(
            int logicalEntityCount,
            SnapshotBuffer[] buffers,
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf)
        {
            int n = _def.Modules.Count;
            int m = _def.Identities != null ? _def.Identities.Count : 0;
            int total = n + m;

            var layout = new Layout
            {
                HeaderFixedSize = ECSaveFormatConstants.HeaderFixedSize,
                WorldIdLength = Encoding.UTF8.GetByteCount(_def.WorldId),
                SectionOffsets = new int[total],
                SectionLengths = new int[total],
                NameOffsets = new int[total],
                NameLengths = new int[total],
                TotalSections = total,
            };

            int headerEnd = layout.HeaderFixedSize + layout.WorldIdLength;
            layout.SectionTableOffset = AlignUp(headerEnd, 8);
            layout.NameHeapOffset = layout.SectionTableOffset + total * ECSaveFormatConstants.SectionEntrySize;

            int nameHeapCursor = 0;
            for (int i = 0; i < n; i++)
            {
                layout.NameOffsets[i] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(_def.Modules[i].FullTypeName);
                layout.NameLengths[i] = len;
                nameHeapCursor += len;
            }
            for (int i = 0; i < m; i++)
            {
                int idx = n + i;
                layout.NameOffsets[idx] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(_def.Identities[i].FullTypeName);
                layout.NameLengths[idx] = len;
                nameHeapCursor += len;
            }
            layout.NameHeapLength = nameHeapCursor;

            layout.SectionsStart = AlignUp(layout.NameHeapOffset + layout.NameHeapLength, 8);

            int cursor = layout.SectionsStart;
            for (int i = 0; i < n; i++)
            {
                layout.SectionOffsets[i] = cursor;

                var mod = _def.Modules[i];
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

                var im = _def.Identities[i];
                int typeDescSize = ComputeTypeDescSize(
                    im.SchemaHashInput.Length, im.FieldNames);
                int bodySize = im.ComputeBodySize(em, idOf);

                layout.SectionLengths[idx] = typeDescSize + bodySize;
                cursor += layout.SectionLengths[idx];
                cursor = AlignUp(cursor, 8);
            }

            layout.FooterOffset = cursor;
            layout.TotalSize = layout.FooterOffset + ECSaveFormatConstants.FooterSize;
            return layout;
        }

        private static int ComputeTypeDescSize(int inputLen, string[] fieldNames)
        {
            int utf8Total = 0;
            for (int i = 0; i < fieldNames.Length; i++)
                utf8Total += Encoding.UTF8.GetByteCount(fieldNames[i]);
            return TypeDescriptorBlockWriter.ComputeSize(inputLen, fieldNames.Length, utf8Total);
        }

        private void WriteSaveData(
            byte* p, in Layout layout,
            NativeList<Entity> entities,
            NativeParallelHashMap<Entity, int> idOf,
            SnapshotBuffer[] buffers,
            EntityManager em,
            CancellationToken ct)
        {
            int n = _def.Modules.Count;
            int m = _def.Identities != null ? _def.Identities.Count : 0;

            var header = new SaveHeader
            {
                Magic = ECSaveFormatConstants.Magic,
                FormatVersion = ECSaveFormatConstants.CurrentFormatVersion,
                HeaderFlags = 0,
                HeaderSize = (uint)(layout.HeaderFixedSize + layout.WorldIdLength),
                GameDataVersion = _def.GameDataVersion,
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

            var worldIdBytes = Encoding.UTF8.GetBytes(_def.WorldId);
            if (worldIdBytes.Length > 0)
            {
                fixed (byte* wid = worldIdBytes)
                    writer.WriteBytes(wid, worldIdBytes.Length);
            }

            writer.Seek(layout.SectionTableOffset);
            for (int i = 0; i < n; i++)
            {
                var mod = _def.Modules[i];
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
                var im = _def.Identities[i];
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

            writer.Seek(layout.NameHeapOffset);
            for (int i = 0; i < n; i++)
            {
                var nameBytes = Encoding.UTF8.GetBytes(_def.Modules[i].FullTypeName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }
            for (int i = 0; i < m; i++)
            {
                var nameBytes = Encoding.UTF8.GetBytes(_def.Identities[i].FullTypeName);
                if (nameBytes.Length > 0)
                {
                    fixed (byte* np = nameBytes)
                        writer.WriteBytes(np, nameBytes.Length);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (ct.IsCancellationRequested) return;

                writer.Seek(layout.SectionOffsets[i]);
                var mod = _def.Modules[i];

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
                if (ct.IsCancellationRequested) return;

                int idx = n + i;
                writer.Seek(layout.SectionOffsets[idx]);
                var im = _def.Identities[i];

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

            int checksumEnd = layout.FooterOffset;
            ulong checksum = ChecksumCalculator.Compute(new ByteView(p, checksumEnd));

            writer.Seek(layout.FooterOffset);
            writer.WriteFooter(checksum);
        }

        private static int AlignUp(int value, int alignment)
        {
            int rem = value % alignment;
            return rem == 0 ? value : value + (alignment - rem);
        }
    }
}
