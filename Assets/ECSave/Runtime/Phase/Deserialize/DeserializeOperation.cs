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
        private readonly RestorationScope _scope;
        private readonly DeletionPolicy _policy;

        public DeserializeOperation(
            SaveDefinition def,
            World world,
            byte[] data,
            RestorationScope scope,
            DeletionPolicy policy)
        {
            _def = def;
            _world = world;
            _data = data;
            _scope = scope;
            _policy = policy;
        }

        public LoadResult Execute(CancellationToken ct)
        {
            var ctx = PrephaseContext.Create(_def, _world);
            try
            {
                var err = D0_InputCopyPhase.Execute(ref ctx, _data, _def, _world, ct);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D1_StructureValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D2_ChecksumVerificationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D3_VersionCheckPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D4_WorldIdCheckPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D5_SchemaMatchPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D6_MigrationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D7_EntityRefValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = D8_RecordValidationPhase.Execute(ref ctx);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                err = P1_PlanPhase.Execute(ref ctx, _policy);
                if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                var destroyTargets = new NativeList<Entity>(16, Allocator.TempJob);
                try
                {
                    err = P1_DeletionPlanPhase.Execute(
                        ref ctx, _scope, _policy, destroyTargets);
                    if (err != SaveLoadErrorCode.None) return LoadResult.Failure(err);

                    return ApplyPhase(ref ctx, destroyTargets, ct);
                }
                finally
                {
                    if (destroyTargets.IsCreated) destroyTargets.Dispose();
                }
            }
            finally
            {
                ctx.Dispose();
            }
        }

        private LoadResult ApplyPhase(
            ref PrephaseContext ctx,
            NativeList<Entity> destroyTargets,
            CancellationToken ct)
        {
            var em = _world.EntityManager;
            var journal = new ApplyJournal(Allocator.Persistent);
            var updateTargets = new NativeList<Entity>(16, Allocator.TempJob);

            try
            {
                for (int i = 0; i < ctx.PersistentToEntity.Length; i++)
                {
                    var e = ctx.PersistentToEntity[i];
                    if (e != Entity.Null) updateTargets.Add(e);
                }

                var updateArray = new NativeArray<Entity>(
                    updateTargets.Length, Allocator.TempJob);

                for (int i = 0; i < updateTargets.Length; i++)
                    updateArray[i] = updateTargets[i];

                try
                {
                    journal.Backup(ctx.Definition, updateArray, em);

                    bool success = false;
                    try
                    {
                        if (FaultInjector.ShouldFail("P3"))
                            throw new InvalidOperationException("Injected P3");

                        CreateEntitiesForNonChunkSections(
                            ref ctx, journal, em);

                        for (int i = 0; i < ctx.SectionTable.Count; i++)
                        {
                            ref var entry = ref ctx.SectionTable.Entries[i];
                            if (entry.Kind != SectionKind.Identity) continue;

                            var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                            CreateEntitiesForSection(
                                bodyView, ctx.PersistentToEntity, journal, em);
                        }

                        if (ct.IsCancellationRequested)
                            throw new InvalidOperationException("Cancelled");

                        if (FaultInjector.ShouldFail("P4"))
                            throw new InvalidOperationException("Injected P4");

                        ApplyNonChunkModules(ref ctx, em);
                        ApplyIdentityModules(ref ctx, em);

                        if (P4_StructuralChangePhase.Execute(ref ctx, _policy, em)
                            != SaveLoadErrorCode.None)
                            throw new InvalidOperationException("P4 removal failed");

                        ApplyChunkModules(ref ctx, em);

                        if (P6_ChunkComponentRestorePhase.Execute(ref ctx, em)
                            != SaveLoadErrorCode.None)
                            throw new InvalidOperationException("P6 failed");

                        if (FaultInjector.ShouldFail("P5"))
                            throw new InvalidOperationException("Injected P5");

                        if (FaultInjector.ShouldFail("P7"))
                            throw new InvalidOperationException("Injected P7");

                        if (destroyTargets.IsCreated && destroyTargets.Length > 0)
                            em.DestroyEntity(destroyTargets.AsArray());

                        success = true;
                    }
                    finally
                    {
                        if (!success)
                        {
                            if (!journal.Rollback(updateArray, em))
                                throw new InvalidOperationException("Rollback failed");
                        }
                    }

                    var warnings = new List<LoadWarning>();
                    ctx.Warnings.ToLoadWarnings(
                        warnings, BuildTypeNameMap(ref ctx));

                    return LoadResult.Success(
                        ctx.Header.LogicalEntityCount, warnings);
                }
                finally
                {
                    if (updateArray.IsCreated) updateArray.Dispose();
                }
            }
            catch (FormatException fe)
            {
                return LoadResult.Failure(ErrorInfoMapper.Map(fe.Error));
            }
            catch
            {
                return LoadResult.Failure(
                    SaveLoadErrorCode.WorldModificationFailed);
            }
            finally
            {
                journal.Dispose();

                if (updateTargets.IsCreated)
                    updateTargets.Dispose();
            }
        }

        private static void CreateEntitiesForNonChunkSections(
            ref PrephaseContext ctx,
            ApplyJournal journal,
            EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (m.SectionKind == SectionKind.ChunkComponent) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                CreateEntitiesForSection(
                    bodyView, ctx.PersistentToEntity, journal, em);
            }
        }

        private static void ApplyNonChunkModules(
            ref PrephaseContext ctx,
            EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (m.SectionKind == SectionKind.ChunkComponent) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                m.ApplySectionBody(
                    bodyView, ctx.PersistentToEntity, em);
            }
        }

        private static void ApplyIdentityModules(
            ref PrephaseContext ctx,
            EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                ref var entry = ref ctx.SectionTable.Entries[i];
                if (entry.Kind != SectionKind.Identity) continue;

                var im = ctx.Definition.FindIdentity(entry.NameHash);
                if (im == null) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                im.ApplySectionBody(
                    bodyView, ctx.PersistentToEntity, em);
            }
        }

        private static void ApplyChunkModules(
            ref PrephaseContext ctx,
            EntityManager em)
        {
            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m == null) continue;
                if (m.SectionKind != SectionKind.ChunkComponent) continue;

                var bodyView = SectionBodyHelper.GetSectionBody(ctx, i);
                m.ApplySectionBody(
                    bodyView, ctx.PersistentToEntity, em);
            }
        }

        private static unsafe void CreateEntitiesForSection(
            ByteView bodyView,
            NativeArray<Entity> persistentToEntity,
            ApplyJournal journal,
            EntityManager em)
        {
            var v = CommonSectionReader.Read(bodyView);
            int count = (int)v.Header.Count;

            for (int k = 0; k < count; k++)
            {
                uint pid = v.EntityIndex[k];
                if (pid >= persistentToEntity.Length)
                    throw new FormatException(
                        FormatError.ValidationFailed,
                        "persistent id out of range");

                if (persistentToEntity[(int)pid] == Entity.Null)
                {
                    var e = em.CreateEntity();
                    persistentToEntity[(int)pid] = e;
                    journal.NoteCreated(e);
                }
            }
        }

        private static Dictionary<uint, string> BuildTypeNameMap(
            ref PrephaseContext ctx)
        {
            var map = new Dictionary<uint, string>();

            for (int i = 0; i < ctx.SectionTable.Count; i++)
            {
                var m = ctx.ModuleBySection[i];
                if (m != null)
                    map[(uint)i] = m.FullTypeName;
            }

            return map;
        }
    }
}
