using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ECSave.Format;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.EditorTools
{
    internal static class InspectionLoader
    {
        public static SaveDataInspection Load(string filePath)
        {
            var inspection = new SaveDataInspection
            {
                FilePath = filePath,
            };

            byte[] data;
            try
            {
                data = File.ReadAllBytes(filePath);
            }
            catch (Exception ex)
            {
                inspection.ErrorMessage = "Failed to read file: " + ex.Message;
                return inspection;
            }

            inspection.Data = data;
            inspection.FileSize = data.Length;

            if (data.Length < ECSaveFormatConstants.HeaderFixedSize + ECSaveFormatConstants.FooterSize)
            {
                inspection.ErrorMessage = "File is too small to be a valid SaveData.";
                return inspection;
            }

            unsafe
            {
                fixed (byte* p = data)
                {
                    try
                    {
                        LoadUnsafe(p, data.Length, inspection);
                        inspection.Loaded = true;
                    }
                    catch (FormatException fe)
                    {
                        inspection.ErrorMessage = "Format error (" + fe.Error + "): " + fe.Message;
                    }
                    catch (Exception ex)
                    {
                        inspection.ErrorMessage = "Error: " + ex.Message;
                    }
                }
            }

            return inspection;
        }

        private static unsafe void LoadUnsafe(byte* p, int length, SaveDataInspection inspection)
        {
            var view = new ByteView(p, length);
            var reader = new FormatReader(view);

            var header = reader.ReadHeader();
            inspection.Magic = header.Magic;
            inspection.FormatVersion = header.FormatVersion;
            inspection.HeaderFlags = header.HeaderFlags;
            inspection.HeaderSize = header.HeaderSize;
            inspection.GameDataVersion = header.GameDataVersion;
            inspection.LogicalEntityCount = header.LogicalEntityCount;
            inspection.SectionCount = header.SectionCount;
            inspection.SectionTableOffset = header.SectionTableOffset;
            inspection.TotalLength = header.TotalLength;
            inspection.NameHeapLength = header.NameHeapLength;
            inspection.WorldIdLength = header.WorldIdLength;

            inspection.WorldId = ReadWorldId(header);

            var table = reader.ReadSectionTable();
            inspection.Sections = ReadSections(view, table, reader);
        }

        private static unsafe string ReadWorldId(in SaveHeader header)
        {
            if (header.WorldIdLength == 0) return string.Empty;
            if (header.WorldIdPtr == null) return string.Empty;
            return Encoding.UTF8.GetString(header.WorldIdPtr, header.WorldIdLength);
        }

        private static unsafe SectionInfo[] ReadSections(
            ByteView view, in SectionTable table, FormatReader reader)
        {
            int n = table.Count;
            var result = new SectionInfo[n];

            for (int i = 0; i < n; i++)
            {
                ref var e = ref table.Entries[i];
                var s = new SectionInfo
                {
                    Index = i,
                    SectionId = e.SectionId,
                    Kind = e.Kind,
                    Flags = e.Flags,
                    NameLength = e.NameLength,
                    NameHash = e.NameHash,
                    SchemaHash = e.SchemaHash,
                    Offset = e.Offset,
                    Length = e.Length,
                    NameOffset = e.NameOffset,
                    HasTypeDescriptor = e.HasTypeDescriptor,
                };

                s.Name = ReadName(table, in e);
                result[i] = s;
            }

            for (int i = 0; i < n; i++)
            {
                var s = result[i];
                try
                {
                    if (s.HasTypeDescriptor)
                        ReadTypeDescriptor(view, reader, i, s);

                    if (s.Kind == SectionKind.ChunkComponent)
                        ReadChunkComponent(view, s);
                }
                catch
                {
                    // 個別セクションの失敗は無視
                }
            }

            return result;
        }

        private static unsafe string ReadName(in SectionTable table, in SectionEntry e)
        {
            if (e.NameLength == 0) return string.Empty;
            if (table.NameHeap == null) return string.Empty;
            if (e.NameOffset + e.NameLength > (uint)table.NameHeapLength) return "(invalid)";

            byte* start = table.NameHeap + e.NameOffset;
            return Encoding.UTF8.GetString(start, e.NameLength);
        }

        private static unsafe void ReadTypeDescriptor(
            ByteView view, FormatReader reader, int index, SectionInfo s)
        {
            var td = reader.ReadTypeDescriptor(index);

            s.TypeDescriptorAvailable = true;
            s.EntryCount = td.EntryCount;
            s.RawEntryCount = td.RawEntryCount;

            if (td.SchemaHashInputLength > 0 && td.SchemaHashInput != null)
            {
                s.SchemaHashInput = new byte[td.SchemaHashInputLength];
                for (int i = 0; i < td.SchemaHashInputLength; i++)
                    s.SchemaHashInput[i] = td.SchemaHashInput[i];
            }
            else
            {
                s.SchemaHashInput = Array.Empty<byte>();
            }

            s.FieldNames = ReadFieldNames(td.NameHeap, td.NameHeapLength, td.EntryCount);
        }

        private static unsafe string[] ReadFieldNames(byte* nameHeap, int length, int entryCount)
        {
            if (entryCount <= 0 || nameHeap == null || length <= 0)
                return Array.Empty<string>();

            var names = new List<string>(entryCount);
            int offset = 0;

            for (int i = 0; i < entryCount; i++)
            {
                if (offset >= length) break;

                byte len = nameHeap[offset];
                offset += 1;

                if (offset + len > length)
                {
                    names.Add("(invalid)");
                    break;
                }

                var name = Encoding.UTF8.GetString(nameHeap + offset, len);
                offset += len;
                names.Add(name);
            }

            return names.ToArray();
        }

        private static unsafe void ReadChunkComponent(ByteView view, SectionInfo s)
        {
            int sectionOffset = (int)s.Offset;
            int sectionLength = (int)s.Length;

            int tdSize = 0;
            if (s.HasTypeDescriptor)
                tdSize = (int)(*(uint*)(view.Pointer + sectionOffset));

            var bodyView = view.Slice(sectionOffset + tdSize, sectionLength - tdSize);

            var chunk = ChunkComponentSectionReader.Read(bodyView);
            s.IsChunkComponent = true;
            s.GroupCount = chunk.Header.GroupCount;
            s.Stride = chunk.Header.Stride;

            int groupCount = (int)chunk.Header.GroupCount;
            if (groupCount > 0)
            {
                s.MemberOffset = new uint[groupCount + 1];
                for (int i = 0; i <= groupCount; i++)
                    s.MemberOffset[i] = chunk.MemberOffset[i];

                int memberCount = (int)chunk.MemberOffset[groupCount];
                if (memberCount > 0)
                {
                    s.MemberEntityIndex = new uint[memberCount];
                    for (int i = 0; i < memberCount; i++)
                        s.MemberEntityIndex[i] = chunk.MemberEntityIndex[i];
                }
                else
                {
                    s.MemberEntityIndex = Array.Empty<uint>();
                }
            }
            else
            {
                s.MemberOffset = new uint[] { 0 };
                s.MemberEntityIndex = Array.Empty<uint>();
            }
        }
    }
}
