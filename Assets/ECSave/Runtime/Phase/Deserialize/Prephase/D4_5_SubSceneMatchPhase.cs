using ECSave.Format;
using ECSave.Internal.SubScene;

namespace ECSave.Phase.Deserialize.Prephase
{
    internal static class D4_5_SubSceneMatchPhase
    {
        public unsafe static SaveLoadErrorCode Execute(
            SubSceneDefinition def,
            SectionTable table,
            ByteView fileView,
            out SubSceneStateSectionView parsed,
            out bool hasSubSceneSection)
        {
            parsed = default;
            hasSubSceneSection = false;

            if (def == null || !def.RegisterSceneLoadState) return SaveLoadErrorCode.None;
            if (table.Entries == null) return SaveLoadErrorCode.None;

            for (int i = 0; i < table.Count; i++)
            {
                var e = table.Entries[i];
                if (e.Kind != SectionKind.SubSceneState) continue;

                if (e.Offset > int.MaxValue || e.Length > int.MaxValue)
                    return SaveLoadErrorCode.CorruptedData;

                int offset = (int)e.Offset;
                int length = (int)e.Length;

                if (!fileView.BoundsCheck(offset, length))
                    return SaveLoadErrorCode.CorruptedData;

                try
                {
                    parsed = SubSceneStateSection.Read(fileView.Pointer + offset, length);
                    hasSubSceneSection = true;
                }
                catch (FormatException)
                {
                    return SaveLoadErrorCode.CorruptedData;
                }

                return SaveLoadErrorCode.None;
            }

            return SaveLoadErrorCode.None;
        }
    }
}
