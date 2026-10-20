using System;
using System.Collections.Generic;
using ECSave.Format;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal.SubScene
{
    internal static class SubSceneStateLayout
    {
        public const int HeaderSize = 8;
        public const int RecordSize = 20;
        public const int GuidHiOffset = 0;
        public const int GuidLoOffset = 8;
        public const int StateOffset = 16;
        public const int ModeOffset = 17;
        public const int ReservedOffset = 18;

        public static int ComputeBodySize(int count)
            => HeaderSize + count * RecordSize;
    }

    internal readonly unsafe struct SubSceneStateSectionView
    {
        public readonly uint Count;
        public readonly byte* Records;

        public SubSceneStateSectionView(uint count, byte* records)
        {
            Count = count;
            Records = records;
        }
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

            long required = SubSceneStateLayout.HeaderSize
                          + (long)count * SubSceneStateLayout.RecordSize;
            if (required > length)
                throw new FormatException(
                    FormatError.CorruptedData,
                    $"SubSceneState section body truncated: count={count}, required={required}, length={length}");

            return new SubSceneStateSectionView(count, pointer + SubSceneStateLayout.HeaderSize);
        }

        public static void Write(ref FormatWriter w, IReadOnlyList<SceneLoadInfo> scenes)
        {
            int count = scenes?.Count ?? 0;
            w.WriteU32((uint)count);
            w.WriteU32(0);
            if (scenes == null) return;

            for (int i = 0; i < count; i++)
            {
                var s = scenes[i];
                w.WriteU64(s.Guid.Hi);
                w.WriteU64(s.Guid.Lo);
                w.WriteU8((byte)s.State);
                w.WriteU8((byte)s.RestoreMode);
                w.WriteU16(0);
            }
        }

        public static SceneGuid GetGuid(in SubSceneStateSectionView v, int i)
        {
            byte* p = v.Records + i * SubSceneStateLayout.RecordSize;
            ulong hi = *(ulong*)(p + SubSceneStateLayout.GuidHiOffset);
            ulong lo = *(ulong*)(p + SubSceneStateLayout.GuidLoOffset);
            return new SceneGuid(hi, lo);
        }

        public static SceneLoadState GetState(in SubSceneStateSectionView v, int i)
            => (SceneLoadState)v.Records[i * SubSceneStateLayout.RecordSize + SubSceneStateLayout.StateOffset];

        public static SceneRestoreMode GetMode(in SubSceneStateSectionView v, int i)
            => (SceneRestoreMode)v.Records[i * SubSceneStateLayout.RecordSize + SubSceneStateLayout.ModeOffset];

        public static SceneLoadInfo GetSceneLoadInfo(in SubSceneStateSectionView v, int i)
            => new SceneLoadInfo(GetGuid(v, i), GetState(v, i), GetMode(v, i));
    }
}
