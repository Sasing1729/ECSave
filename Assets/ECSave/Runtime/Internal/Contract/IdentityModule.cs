using ECSave.Format;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    public abstract class IdentityModule
    {
        public int RegistrationId { get; internal set; }
        public abstract ulong SchemaHash { get; }
        public abstract string FullTypeName { get; }
        public abstract int KeyStride { get; }
        public abstract byte[] SchemaHashInput { get; }
        public abstract string[] FieldNames { get; }

        public abstract EntityQuery BuildQuery(EntityManager em);

        public abstract int ComputeBodySize(
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf);

        public abstract void WriteSectionBody(
            ref FormatWriter writer,
            EntityManager em,
            NativeParallelHashMap<Entity, int> idOf);

        /// <summary>
        /// World 側の識別キー索引を構築する。
        ///
        /// <para>
        /// <b>scopeFilter</b> が IsCreated な場合、そこに含まれる Entity のみを
        /// 索引に登録する。これは <see cref="RestorationScope.Custom"/> による
        /// 「復元領域内の Entity のみを識別照合の対象にする」契約を実現する
        /// （設計書 9.2 節）。
        /// </para>
        /// <para>
        /// <b>scopeFilter</b> が未作成（default）の場合、クエリ結果の全 Entity を
        /// 索引に登録する（<see cref="RestorationScope.EntireWorld"/> の従来挙動）。
        /// </para>
        /// </summary>
        public abstract SaveLoadErrorCode BuildWorldIndex(
            EntityManager em,
            EntityQuery query,
            ref IdentityIndex index,
            uint typeIndex,
            NativeParallelHashSet<Entity> scopeFilter);

        public abstract SaveLoadErrorCode BuildSaveIndex(
            ByteView sectionBody,
            ref IdentityIndex index,
            uint typeIndex);

        public abstract void ApplySectionBody(
            ByteView sectionBody,
            NativeArray<Entity> persistentToEntity,
            EntityManager em);
    }
}
