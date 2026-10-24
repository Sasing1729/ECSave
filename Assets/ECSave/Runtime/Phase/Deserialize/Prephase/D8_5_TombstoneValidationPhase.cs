using System.Collections.Generic;
using ECSave.Format;
using ECSave.Internal;
using ECSave.Internal.SubScene;

namespace ECSave.Phase.Deserialize.Prephase
{
    internal static unsafe class D8_5_TombstoneValidationPhase
    {
        public struct TombstoneEntry
        {
            public SubSceneDestroyedIdentityHandler Handler;
            public SubSceneTombstoneSectionView View;
        }

        public static SaveLoadErrorCode Execute(
            ref PrephaseContext ctx,
            out IReadOnlyList<TombstoneEntry> entries)
        {
            var result = new List<TombstoneEntry>();
            entries = result;

            var def = ctx.Definition.SubScene;
            if (def == null || !def.HasTombstones)
                return SaveLoadErrorCode.None;

            var handlerByHash = new Dictionary<ulong, SubSceneDestroyedIdentityHandler>(
                def.DestroyedHandlers.Count);
            for (int i = 0; i < def.DestroyedHandlers.Count; i++)
            {
                var h = def.DestroyedHandlers[i];
                if (h == null) continue;
                handlerByHash[SubSceneTombstoneSection.NameHashFor(h.FullTypeName)] = h;
            }

            for (int s = 0; s < ctx.SectionTable.Count; s++)
            {
                ref var entry = ref ctx.SectionTable.Entries[s];
                if (entry.Kind != SectionKind.SubSceneTombstone) continue;

                if (!handlerByHash.TryGetValue(entry.NameHash, out var handler))
                    continue;

                if (entry.Offset > int.MaxValue || entry.Length > int.MaxValue)
                    return SaveLoadErrorCode.CorruptedData;

                int offset = (int)entry.Offset;
                int length = (int)entry.Length;

                if (!ctx.View.BoundsCheck(offset, length))
                    return SaveLoadErrorCode.CorruptedData;

                SubSceneTombstoneSectionView view;
                try
                {
                    view = SubSceneTombstoneSection.Read(ctx.View.Pointer + offset, length);
                }
                catch (FormatException)
                {
                    return SaveLoadErrorCode.CorruptedData;
                }

                if (view.KeyStride != handler.KeyStride)
                    return SaveLoadErrorCode.ValidationFailed;

                // Review #5a: O(n²) 比較を HashSet<byte[]> でハッシュ化。
                // Tombstone 件数は数百程度なので、マネージド HashSet で十分。
                var seen = new HashSet<byte[]>(ByteArrayComparer.Instance);
                int n = (int)view.Count;
                int stride = view.KeyStride;

                for (int a = 0; a < n; a++)
                {
                    var key = new byte[stride];
                    byte* pa = view.Keys + a * stride;
                    for (int b = 0; b < stride; b++) key[b] = pa[b];

                    if (!seen.Add(key))
                        return SaveLoadErrorCode.ValidationFailed;
                }

                result.Add(new TombstoneEntry { Handler = handler, View = view });
            }

            return SaveLoadErrorCode.None;
        }

        private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
        {
            public static readonly ByteArrayComparer Instance = new ByteArrayComparer();

            public bool Equals(byte[] x, byte[] y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x == null || y == null) return false;
                if (x.Length != y.Length) return false;
                for (int i = 0; i < x.Length; i++)
                    if (x[i] != y[i]) return false;
                return true;
            }

            public int GetHashCode(byte[] obj)
            {
                if (obj == null) return 0;
                ulong h = 0xcbf29ce484222325UL;
                for (int i = 0; i < obj.Length; i++)
                {
                    h ^= obj[i];
                    h *= 0x100000001b3UL;
                }
                return (int)(h ^ (h >> 32));
            }
        }
    }
}
