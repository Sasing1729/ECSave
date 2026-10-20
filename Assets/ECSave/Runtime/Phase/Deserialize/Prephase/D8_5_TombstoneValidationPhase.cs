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

                int n = (int)view.Count;
                int stride = view.KeyStride;
                for (int a = 0; a < n; a++)
                {
                    byte* pa = view.Keys + a * stride;
                    for (int b = a + 1; b < n; b++)
                    {
                        byte* pb = view.Keys + b * stride;
                        bool eq = true;
                        for (int k = 0; k < stride; k++)
                        {
                            if (pa[k] != pb[k]) { eq = false; break; }
                        }
                        if (eq) return SaveLoadErrorCode.ValidationFailed;
                    }
                }

                result.Add(new TombstoneEntry { Handler = handler, View = view });
            }

            return SaveLoadErrorCode.None;
        }
    }
}
