using System;
using System.Collections.Generic;
using System.Threading;
using ECSave.Format;
using ECSave.Internal;
using Unity.Collections;
using Unity.Entities;
using FormatError = ECSave.Format.FormatError;
using FormatException = ECSave.Format.FormatException;

namespace ECSave.Internal
{
    internal sealed unsafe class DeserializeOperation
    {
        private readonly SaveDefinition _def;
        private readonly World _world;
        private readonly byte[] _data;
        private readonly DeletionPolicy _policy;

        public DeserializeOperation(
            SaveDefinition def, World world, byte[] data, DeletionPolicy policy)
        {
            _def = def;
            _world = world;
            _data = data;
            _policy = policy;
        }

        public LoadResult Execute(CancellationToken ct)
        {
            var ctx = PrephaseContext.Create(_def, _world);
            try
            {
                var err = D0_InputCopyPhase.Execute(ref ctx, _data, _def, _world, ct);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                err = D1_StructureValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                err = D2_ChecksumVerificationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                err = D3_VersionCheckPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                err = D4_WorldIdCheckPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                err = D5_SchemaMatchPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                err = D6_MigrationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                err = D7_EntityRefValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                err = D8_RecordValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None)
                    return LoadResult.Failure(err);

                return ApplyPhase(ref ctx, ct);
            }
            finally
            {
                ctx.Dispose();
            }
        }

        private LoadResult ApplyPhase(ref PrephaseContext ctx, CancellationToken ct)
        {
            var em = _world.EntityManager;

            try
            {
                for (int i = 0; i < ctx.SectionTable.Count; i++)
                {
                    var m = ctx.ModuleBySection[i];
                    if (m == null) continue;

                    var bodyView = GetSectionBody(ref ctx, i);
                    CreateEntitiesForSection(bodyView, ctx.PersistentToEntity, em);
                }

                for (int i = 0; i < ctx.SectionTable.Count; i++)
                {
                    if (ct.IsCancellationRequested)
                        return LoadResult.Failure(SaveLoadErrorCode.Cancelled);

                    var m = ctx.ModuleBySection[i];
                    if (m == null) continue;

                    var bodyView = GetSectionBody(ref ctx, i);
                    m.ApplySectionBody(bodyView, ctx.PersistentToEntity, em);
                }

                var warnings = new List<LoadWarning>();
                ctx.Warnings.ToLoadWarnings(warnings, BuildTypeNameMap(ref ctx));

                return LoadResult.Success(ctx.Header.LogicalEntityCount, warnings);
            }
            catch (FormatException fe)
            {
                return LoadResult.Failure(ErrorInfoMapper.Map(fe.Error));
            }
            catch (Exception)
            {
                return LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed);
            }
        }

        private static ByteView GetSectionBody(ref PrephaseContext ctx, int sectionIndex)
        {
            ref var entry = ref ctx.SectionTable.Entries[sectionIndex];
            var sectionView = new ByteView(
                ctx.View.Pointer + (int)entry.Offset, (int)entry.Length);

            int tdSize = (int)(*(uint*)sectionView.Pointer);
            return sectionView.Slice(tdSize, (int)entry.Length - tdSize);
        }

        private static unsafe void CreateEntitiesForSection(
            ByteView bodyView,
            NativeArray<Entity> persistentToEntity,
            EntityManager em)
        {
            var v = CommonSectionReader.Read(bodyView);
            int count = (int)v.Header.Count;

            for (int k = 0; k < count; k++)
            {
                uint pid = v.EntityIndex[k];
                if (pid >= persistentToEntity.Length)
                    throw new FormatException(FormatError.ValidationFailed,
                        "persistent id out of range");

                if (persistentToEntity[(int)pid] == Entity.Null)
                    persistentToEntity[(int)pid] = em.CreateEntity();
            }
        }

        private static Dictionary<uint, string> BuildTypeNameMap(ref PrephaseContext ctx)
        {
            var map = new Dictionary<uint, string>();
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m != null) map[(uint)i] = m.FullTypeName;
            }
            return map;
        }
    }
}
