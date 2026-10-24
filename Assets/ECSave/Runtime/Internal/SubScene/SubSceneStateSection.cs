using System;
using System.Collections.Generic;
using ECSave.Format;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal.SubScene
{
    internal static class SubSceneStateLayout
    {
        public const int HeaderSize = 8;
        public const int RecordSizeNoHash = 20;
        public const int RecordSizeWithHash = 28;

        public const int GuidHiOffset = 0;
        public const int GuidLoOffset = 8;
        public const int StateOffset = 16;
        public const int ModeOffset = 17;
        public const int ReservedOffset = 18;
        public const int ContentHashOffset = 20;

        public const uint Flag_HasContentHash = 1u << 0;

        public static int RecordSize(bool hasContentHash)
            => hasContentHash ? RecordSizeWithHash : RecordSizeNoHash;

        public static int ComputeBodySize(int count, bool hasContentHash)
            => HeaderSize + count * RecordSize(hasContentHash);

        public static int ComputeBodySize(IReadOnlyList<SceneLoadInfo> scenes)
        {
            if (scenes == null || scenes.Count == 0) return HeaderSize;
            bool hasHash = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].ContentHash != 0UL) { hasHash = true; break; }
            }
            return ComputeBodySize(scenes.Count, hasHash);
        }
    }

    internal readonly unsafe struct SubSceneStateSectionView
    {
        public readonly uint Count;
        public readonly bool HasContentHash;
        public readonly byte* Records;

        public SubSceneStateSectionView(uint count, bool hasContentHash, byte* records)
        {
            Count = count;
            HasContentHash = hasContentHash;
            Records = records;
        }

        public int RecordSize => SubSceneStateLayout.RecordSize(HasContentHash);
    }

    internal static unsafe class SubSceneStateSection
    {
        public const string SectionName = "ECSave.SubSceneState";

        public static SubSceneStateSectionView Read(byte* pointer, int length)
        {
            if (pointer == null || length < SubSceneStateLayout.HeaderSize)
                throw new FormatException(
                    FormatError.CorruptedData,
                    $"SubSceneState section body too short: length={length}, header={SubSceneStateLayout.HeaderSize}");

            uint count = *(uint*)pointer;
            uint flags = *(uint*)(pointer + 4);
            bool hasHash = (flags & SubSceneStateLayout.Flag_HasContentHash) != 0;

            int recordSize = SubSceneStateLayout.RecordSize(hasHash);
            long required = SubSceneStateLayout.HeaderSize + (long)count * recordSize;
            if (required > length)
                throw new FormatException(
                    FormatError.CorruptedData,
                    $"SubSceneState section body truncated: count={count}, hasHash={hasHash}, required={required}, length={length}");

            return new SubSceneStateSectionView(
                count, hasHash, pointer + SubSceneStateLayout.HeaderSize);
        }

        public static void Write(ref FormatWriter w, IReadOnlyList<SceneLoadInfo> scenes)
        {
            int count = scenes?.Count ?? 0;

            bool hasHash = false;
            if (scenes != null)
            {
                for (int i = 0; i < count; i++)
                {
                    if (scenes[i].ContentHash != 0UL) { hasHash = true; break; }
                }
            }

            uint flags = hasHash ? SubSceneStateLayout.Flag_HasContentHash : 0u;
            w.WriteU32((uint)count);
            w.WriteU32(flags);

            if (scenes == null) return;

            for (int i = 0; i < count; i++)
            {
                var s = scenes[i];
                w.WriteU64(s.Guid.Hi);
                w.WriteU64(s.Guid.Lo);
                w.WriteU8((byte)s.State);
                w.WriteU8((byte)s.RestoreMode);
                w.WriteU16(0);
                if (hasHash)
                    w.WriteU64(s.ContentHash);
            }
        }

        public static SceneGuid GetGuid(in SubSceneStateSectionView v, int i)
        {
            byte* p = v.Records + i * v.RecordSize;
            ulong hi = *(ulong*)(p + SubSceneStateLayout.GuidHiOffset);
            ulong lo = *(ulong*)(p + SubSceneStateLayout.GuidLoOffset);
            return new SceneGuid(hi, lo);
        }

        public static SceneLoadState GetState(in SubSceneStateSectionView v, int i)
            => (SceneLoadState)v.Records[i * v.RecordSize + SubSceneStateLayout.StateOffset];

        public static SceneRestoreMode GetMode(in SubSceneStateSectionView v, int i)
            => (SceneRestoreMode)v.Records[i * v.RecordSize + SubSceneStateLayout.ModeOffset];

        public static ulong GetContentHash(in SubSceneStateSectionView v, int i)
        {
            if (!v.HasContentHash) return 0UL;
            byte* p = v.Records + i * v.RecordSize + SubSceneStateLayout.ContentHashOffset;
            return *(ulong*)p;
        }

        public static SceneLoadInfo GetSceneLoadInfo(in SubSceneStateSectionView v, int i)
            => new SceneLoadInfo(GetGuid(v, i), GetState(v, i), GetMode(v, i), GetContentHash(v, i));
    }
}
