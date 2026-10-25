using System.Text;
using Unity.Entities;

namespace ECSave
{
    public readonly struct LoadWarning
    {
        public LoadWarningKind Kind { get; }
        public Entity SourceEntity { get; }
        public string TypeName { get; }
        public int SaveEntityIndex { get; }
        public ulong ExtraHash { get; }

        public LoadWarning(LoadWarningKind kind, Entity sourceEntity, string typeName,
            int saveEntityIndex, ulong extraHash = 0UL)
        {
            Kind = kind;
            SourceEntity = sourceEntity;
            TypeName = typeName;
            SaveEntityIndex = saveEntityIndex;
            ExtraHash = extraHash;
        }

        /// <summary>
        /// Kind ごとに意味のある文字列を返す。
        ///
        /// <para>
        /// <b>設計意図</b>: ログ・診断画面・ユーザー通知で読みやすい形式にする。
        /// 機械可読な詳細（<see cref="ExtraHash"/> 等）はフィールドとして残るため、
        /// 必要な場合は構造化ログで個別に取り出すこと。
        /// </para>
        /// </summary>
        public override string ToString()
        {
            var typeName = TypeName ?? "<unknown>";

            switch (Kind)
            {
                case LoadWarningKind.EntityReferenceUnresolved:
                    return $"{typeName}: unresolved entity reference at save index {SaveEntityIndex}";

                case LoadWarningKind.UnknownSectionIgnored:
                    return $"Unknown section ignored (index {SaveEntityIndex}, type={typeName})";

                case LoadWarningKind.ChunkComponentMerged:
                    return $"{typeName}: chunk component merged (save index {SaveEntityIndex})";

                case LoadWarningKind.SceneLoadStateMismatch:
                    return $"Scene load state mismatch (save index {SaveEntityIndex})";

                case LoadWarningKind.SceneRestoreFailed:
                    return $"Scene restore failed (save index {SaveEntityIndex})";

                case LoadWarningKind.SceneContentHashMismatch:
                    return $"Scene content hash mismatch (save index {SaveEntityIndex})";

                case LoadWarningKind.PrefabMigrated:
                    return $"Prefab migrated (save index {SaveEntityIndex})";

                case LoadWarningKind.SceneStateRolledBack:
                    return "Scene state rolled back after load failure";

                case LoadWarningKind.FixedStringMayBeTruncated:
                    return $"{typeName}: FixedString may be truncated (save index {SaveEntityIndex})";

                case LoadWarningKind.PrefabSchemaNotRegistered:
                    return $"Prefab schema not registered (GUID=0x{ExtraHash:X16})";

                case LoadWarningKind.ByScopeDeletionWarning:
                    return $"ByScope deletion destroyed {ExtraHash} entities";

                case LoadWarningKind.PrefabSchemaMigrationFailed:
                    return $"{typeName}: prefab schema migration failed (save index {SaveEntityIndex})";

                case LoadWarningKind.ChunkComponentEntityMissing:
                    return $"{typeName}: {ExtraHash} chunk component entities were missing at load " +
                           $"(first save index {SaveEntityIndex})";

                default:
                    // 将来 enum に追加された値のフォールバック
                    var sb = new StringBuilder(96);
                    sb.Append(Kind);
                    sb.Append(": Type=");
                    sb.Append(typeName);
                    sb.Append(", Source=");
                    sb.Append(SourceEntity);
                    sb.Append(", SaveIndex=");
                    sb.Append(SaveEntityIndex);
                    if (ExtraHash != 0UL)
                    {
                        sb.Append(", Extra=0x");
                        sb.Append(ExtraHash.ToString("X16"));
                    }
                    return sb.ToString();
            }
        }
    }
}
