using System;
using System.Collections.Generic;
using ECSave.Format;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal.SubScene
{
    internal static class SubSceneTombstoneLayout
    {
        public const int HeaderSize = 12;
        public const int CountOffset = 0;
        public const int KeyStrideOffset = 4;
        public const int ReservedOffset = 8;

        public static int ComputeBodySize(int count, int keyStride)
            => HeaderSize + count * keyStride;
    }

    internal readonly unsafe struct SubSceneTombstoneSectionView
    {
        public readonly uint Count;
        public readonly int KeyStride;
        public readonly byte* Keys;

        public SubSceneTombstoneSectionView(uint count, int keyStride, byte* keys)
        {
            Count = count;
            KeyStride = keyStride;
            Keys = keys;
        }

        public byte* GetKey(int i) => Keys + i * KeyStride;
    }

    internal static unsafe class SubSceneTombstoneSection
    {
        public const string SectionNamePrefix = "ECSave.SubSceneTombstone:";

        public static string SectionNameFor(string identityFullTypeName)
            => SectionNamePrefix + identityFullTypeName;

        public static ulong NameHashFor(string identityFullTypeName)
            => SchemaHashCoreLite.HashUtf8(SectionNameFor(identityFullTypeName));

        public static SubSceneTombstoneSectionView Read(byte* pointer, int length)
        {
            if (pointer == null || length < SubSceneTombstoneLayout.HeaderSize)
                throw new FormatException(
                    FormatError.CorruptedData,
                    $"SubSceneTombstone section too short: length={length}, header={SubSceneTombstoneLayout.HeaderSize}");

            uint count = *(uint*)pointer;
            int keyStride = *(int*)(pointer + SubSceneTombstoneLayout.KeyStrideOffset);

            if (keyStride < 0 || keyStride > ECSaveFormatConstants.MaxStructSize)
                throw new FormatException(FormatError.CorruptedData,
                    $"SubSceneTombstone keyStride out of range: {keyStride}");

            long required = SubSceneTombstoneLayout.HeaderSize + (long)count * keyStride;
            if (required > length)
                throw new FormatException(FormatError.CorruptedData,
                    $"SubSceneTombstone section truncated: count={count}, stride={keyStride}, required={required}, length={length}");

            return new SubSceneTombstoneSectionView(
                count, keyStride, pointer + SubSceneTombstoneLayout.HeaderSize);
        }

        public static void Write(ref FormatWriter w, IReadOnlyList<byte[]> keys, int keyStride)
        {
            int count = keys?.Count ?? 0;
            w.WriteU32((uint)count);
            w.WriteI32(keyStride);
            w.WriteU32(0);

            if (count == 0) return;

            for (int i = 0; i < count; i++)
            {
                var key = keys[i];
                if (key == null || key.Length != keyStride)
                    throw new FormatException(FormatError.CorruptedData,
                        $"Tombstone key {i} has invalid length: {(key?.Length ?? -1)} != {keyStride}");

                fixed (byte* p = key)
                    w.WriteBytes(p, keyStride);
            }
        }
    }
}
