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

            var buffers = new SnapshotBuffer[n];

            try
            {
                SerializeDiag.BeginPhase();
                for (int i = 0; i < n; i++)
                {
                    if (ct.IsCancellationRequested)
                        return SaveResult.Failure(SaveLoadErrorCode.Cancelled);

                    var m = _def.Modules[i];
                    buffers[i] = new SnapshotBuffer(256, m.RawRecordStride, Allocator.TempJob);

                    if (m.HasEnabledBits)
                        buffers[i].InitEnabledStates(Allocator.TempJob);

                    var query = m.BuildQuery(em);
                    m.Capture(query, em, ref buffers[i]);
                }
                SerializeDiag.EndPhase("Capture");

                int totalUpperBound = 0;
                for (int i = 0; i < n; i++)
                    totalUpperBound += buffers[i].Count;

                int initialCap = totalUpperBound > 0 ? totalUpperBound : 16;

                var idOf = new NativeParallelHashMap<Entity, int>(initialCap, Allocator.TempJob);
                var entityList = new NativeList<Entity>(initialCap, Allocator.TempJob);

                try
                {
                    SerializeDiag.BeginPhase();
                    for (int i = 0; i < n; i++)
                    {
                        var entities = buffers[i].Entities;
                        for (int j = 0; j < entities.Length; j++)
                        {
                            var e = entities[j];
                            if (idOf.TryAdd(e, entityList.Length))
                                entityList.Add(e);
                        }
                    }
                    SerializeDiag.EndPhase("idOf 構築");

                    int logicalEntityCount = entityList.Length;

                    SerializeDiag.BeginPhase();
                    var layout = ComputeLayout(logicalEntityCount, buffers);
                    SerializeDiag.EndPhase("ComputeLayout");

                    SerializeDiag.BeginPhase();
                    byte[] result = new byte[layout.TotalSize];
                    SerializeDiag.EndPhase("byte[] 確保");

                    fixed (byte* p = result)
                    {
                        WriteSaveData(p, layout, entityList, idOf, buffers, ct);
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
        }

        private Layout ComputeLayout(int logicalEntityCount, SnapshotBuffer[] buffers)
        {
            int n = _def.Modules.Count;
            var layout = new Layout
            {
                HeaderFixedSize = ECSaveFormatConstants.HeaderFixedSize,
                WorldIdLength = Encoding.UTF8.GetByteCount(_def.WorldId),
                SectionOffsets = new int[n],
                SectionLengths = new int[n],
                NameOffsets = new int[n],
                NameLengths = new int[n],
            };

            int headerEnd = layout.HeaderFixedSize + layout.WorldIdLength;
            layout.SectionTableOffset = AlignUp(headerEnd, 8);
            layout.NameHeapOffset = layout.SectionTableOffset + n * ECSaveFormatConstants.SectionEntrySize;

            int nameHeapCursor = 0;
            for (int i = 0; i < n; i++)
            {
                layout.NameOffsets[i] = nameHeapCursor;
                int len = Encoding.UTF8.GetByteCount(_def.Modules[i].FullTypeName);
                layout.NameLengths[i] = len;
                nameHeapCursor += len;
            }
            layout.NameHeapLength = nameHeapCursor;

            layout.SectionsStart = AlignUp(layout.NameHeapOffset + layout.NameHeapLength, 8);

            int cursor = layout.SectionsStart;
            for (int i = 0; i < n; i++)
            {
                layout.SectionOffsets[i] = cursor;

                var m = _def.Modules[i];
                int typeDescSize = ComputeTypeDescSize(m);
                int bodySize = m.ComputeBodySize(buffers[i]);

                layout.SectionLengths[i] = typeDescSize + bodySize;
                cursor += layout.SectionLengths[i];
                cursor = AlignUp(cursor, 8);
            }

            layout.FooterOffset = cursor;
            layout.TotalSize = layout.FooterOffset + ECSaveFormatConstants.FooterSize;
            return layout;
        }

        private static int ComputeTypeDescSize(SaveTypeModule m)
        {
            int inputLen = m.SchemaHashInput.Length;
            int utf8Total = 0;
            foreach (var name in m.FieldNames)
                utf8Total += Encoding.UTF8.GetByteCount(name);
            return TypeDescriptorBlockWriter.ComputeSize(inputLen, m.FieldNames.Length, utf8Total);
        }

        private void WriteSaveData(
            byte* p, in Layout layout,
            NativeList<Entity> entities,
            NativeParallelHashMap<Entity, int> idOf,
            SnapshotBuffer[] buffers,
            CancellationToken ct)
        {
            int n = _def.Modules.Count;

            SerializeDiag.BeginPhase();
            var header = new SaveHeader
            {
                Magic = ECSaveFormatConstants.Magic,
                FormatVersion = ECSaveFormatConstants.CurrentFormatVersion,
                HeaderFlags = 0,
                HeaderSize = (uint)(layout.HeaderFixedSize + layout.WorldIdLength),
                GameDataVersion = _def.GameDataVersion,
                LogicalEntityCount = entities.Length,
                SectionCount = n,
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
                var m = _def.Modules[i];
                var entry = new SectionEntry
                {
                    SectionId = (uint)i,
                    Kind = m.SectionKind,
                    Flags = ECSaveFormatConstants.SectionEntryFlags_HasTypeDescriptor,
                    NameLength = (ushort)layout.NameLengths[i],
                    NameHash = SchemaHashCore.HashUtf8(m.FullTypeName),
                    SchemaHash = m.SchemaHash,
                    Offset = (ulong)layout.SectionOffsets[i],
                    Length = (ulong)layout.SectionLengths[i],
                    NameOffset = (uint)layout.NameOffsets[i],
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
            SerializeDiag.EndPhase("WriteSaveData: ヘッダ・表・名前");

            SerializeDiag.BeginPhase();
            for (int i = 0; i < n; i++)
            {
                if (ct.IsCancellationRequested) return;

                writer.Seek(layout.SectionOffsets[i]);
                var m = _def.Modules[i];

                var input = m.SchemaHashInput;
                fixed (byte* inputPtr = input)
                {
                    TypeDescriptorBlockWriter.Write(
                        ref writer,
                        entryCount: (ushort)m.FieldNames.Length,
                        rawEntryCount: 0,
                        schemaHashInput: inputPtr,
                        schemaHashInputLength: input.Length,
                        fieldNames: m.FieldNames);
                }

                m.WriteSectionBody(ref writer, buffers[i], idOf);
            }
            SerializeDiag.EndPhase("WriteSaveData: セクション本体");

            SerializeDiag.BeginPhase();
            int checksumEnd = layout.FooterOffset;
            ulong checksum = ChecksumCalculator.Compute(new ByteView(p, checksumEnd));
            SerializeDiag.EndPhase("Checksum");

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
