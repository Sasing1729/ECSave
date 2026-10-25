//using System;
//using System.Collections.Generic;
//using System.Linq;

//namespace ECSave
//{
//    /// <summary>
//    /// <see cref="LoadWarning"/> のグルーピング・分類を提供する拡張メソッド。
//    /// </summary>
//    public static class LoadWarningExtensions
//    {
//        /// <summary>
//        /// Kind ごとにグルーピングした辞書を返す。
//        /// 大量の警告（例: Entity 参照未解決が数千件）を種類別に集計する用途。
//        /// </summary>
//        public static IReadOnlyDictionary<LoadWarningKind, IReadOnlyList<LoadWarning>> GroupByKind(
//            this IEnumerable<LoadWarning> warnings)
//        {
//            if (warnings == null)
//                return new Dictionary<LoadWarningKind, IReadOnlyList<LoadWarning>>();

//            return warnings
//                .GroupBy(w => w.Kind)
//                .ToDictionary(g => g.Key, g => (IReadOnlyList<LoadWarning>)g.ToList());
//        }

//        /// <summary>指定 Kind の警告のみを返す。</summary>
//        public static IReadOnlyList<LoadWarning> OfKind(
//            this IEnumerable<LoadWarning> warnings, LoadWarningKind kind)
//        {
//            if (warnings == null) return Array.Empty<LoadWarning>();
//            return warnings.Where(w => w.Kind == kind).ToList();
//        }

//        /// <summary>指定 Kind の警告件数を返す。</summary>
//        public static int CountOf(
//            this IEnumerable<LoadWarning> warnings, LoadWarningKind kind)
//        {
//            if (warnings == null) return 0;
//            return warnings.Count(w => w.Kind == kind);
//        }

//        /// <summary>
//        /// 重要な警告（ユーザーに通知すべきもの）かどうか。
//        ///
//        /// <para>
//        /// <b>true の例</b>: Entity 参照未解決、未知セクション、FixedString 切詰め、
//        /// ChunkComponent マージ／entity 欠落 など。データが失われた／失われる可能性がある。
//        /// </para>
//        /// <para>
//        /// <b>false の例</b>: PrefabMigrated（成功裏に移行された）、
//        /// SceneStateRolledBack（意図した復元動作）。
//        /// </para>
//        /// </summary>
//        public static bool IsCritical(this LoadWarningKind kind) => kind switch
//        {
//            LoadWarningKind.EntityReferenceUnresolved => true,
//            LoadWarningKind.UnknownSectionIgnored => true,
//            LoadWarningKind.ChunkComponentMerged => true,
//            LoadWarningKind.SceneRestoreFailed => true,
//            LoadWarningKind.SceneContentHashMismatch => true,
//            LoadWarningKind.FixedStringMayBeTruncated => true,
//            LoadWarningKind.PrefabSchemaNotRegistered => true,
//            LoadWarningKind.ByScopeDeletionWarning => true,
//            LoadWarningKind.PrefabSchemaMigrationFailed => true,
//            LoadWarningKind.ChunkComponentEntityMissing => true,
//            _ => false,
//        };
//    }
//}
