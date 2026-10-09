using System.Diagnostics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SaveSpike
{
    public static class SpikeToggles
    {
        public static bool GameWriterEnabled;
        public static bool MainThreadProbe;
        public static Entity ProbeEntity;
        public static long ProbeWaitTicks;
    }

    public static class SnapshotExchange
    {
        public static bool RequestPending;
        public static bool Ready;
        public static SpikeColumns Result;
        public static JobHandle Handle;
        public static long ScheduleTicks;
    }

    public static class SpikeThresholds
    {
        public const double S1IdealMs = 16.7;
        public const double S1AllowedMs = 33.0;
        public const double S2ApplyProvisionalMs = 2000.0;
        public const double TotalLoadMs = 5000.0;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(GameWriterSystem))]
    public partial struct SnapshotSystem : ISystem
    {
        EntityQuery _query;

        // type handles をキャッシュ
        EntityTypeHandle _entityH;
        ComponentTypeHandle<SpikeId> _idH;
        ComponentTypeHandle<SpikeHealth> _healthH;
        ComponentTypeHandle<SpikeTarget> _targetH;
        ComponentTypeHandle<SpikeStunned> _stunnedH;
        BufferTypeHandle<SpikeInventory> _invH;
        ComponentTypeHandle<SpikePosition> _posH;
        ComponentTypeHandle<SpikePlayer> _playerH;

        public void OnCreate(ref SystemState state)
        {
            var b = new EntityQueryBuilder(Allocator.Temp);
            b.WithAny<SpikeId, SpikeHealth, SpikeTarget, SpikeStunned, SpikeInventory, SpikePosition>();
            b.WithOptions(EntityQueryOptions.IgnoreComponentEnabledState);
            _query = state.GetEntityQuery(b);
            b.Dispose();

            // ここで作る（OnUpdate で作ると警告）
            _entityH = state.GetEntityTypeHandle();
            _idH = state.GetComponentTypeHandle<SpikeId>(true);
            _healthH = state.GetComponentTypeHandle<SpikeHealth>(true);
            _targetH = state.GetComponentTypeHandle<SpikeTarget>(true);
            _stunnedH = state.GetComponentTypeHandle<SpikeStunned>(true);
            _invH = state.GetBufferTypeHandle<SpikeInventory>(true);
            _posH = state.GetComponentTypeHandle<SpikePosition>(true);
            _playerH = state.GetComponentTypeHandle<SpikePlayer>(true);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SnapshotExchange.RequestPending) return;
            SnapshotExchange.RequestPending = false;

            // type handles を更新
            _entityH.Update(ref state);
            _idH.Update(ref state);
            _healthH.Update(ref state);
            _targetH.Update(ref state);
            _stunnedH.Update(ref state);
            _invH.Update(ref state);
            _posH.Update(ref state);
            _playerH.Update(ref state);

            var sw = Stopwatch.StartNew();

            int count = _query.CalculateEntityCount();
            var cols = SpikeColumns.Allocate(count, count * SpikeLayout.MaxInvPerEntity, true);

            var baseIdx = _query.CalculateBaseEntityIndexArrayAsync(
                Allocator.TempJob, state.Dependency, out var h0);

            var fixedJob = new SnapshotFixedJob
            {
                BaseIndex = baseIdx,
                EntityH = _entityH,
                IdH = _idH,
                HealthH = _healthH,
                TargetH = _targetH,
                StunnedH = _stunnedH,
                InvH = _invH,
                PosH = _posH,
                PlayerH = _playerH,
                Entities = cols.Entities,
                Flags = cols.Flags,
                Ids = cols.Ids,
                Health = cols.Health,
                TargetEntity = cols.TargetEntity,
                TargetIdx = cols.TargetIdx,
                InvOffset = cols.InvOffset,
                Pos = cols.Pos,
            };
            var h1 = fixedJob.Schedule(_query, h0);

            var h2 = new PrefixJob { Count = count, InvOffset = cols.InvOffset }.Schedule(h1);

            var invJob = new SnapshotInvJob
            {
                BaseIndex = baseIdx,
                InvH = _invH,
                InvOffset = cols.InvOffset,
                InvElems = cols.InvElems,
            };
            var h3 = invJob.Schedule(_query, h2);

            baseIdx.Dispose(h3);
            state.Dependency = h3;

            SnapshotExchange.Result = cols;
            SnapshotExchange.Handle = h3;
            SnapshotExchange.Ready = true;
            SnapshotExchange.ScheduleTicks = sw.ElapsedTicks;
        }
    }

    [BurstCompile]
    struct SnapshotFixedJob : IJobChunk
    {
        [ReadOnly] public NativeArray<int> BaseIndex;
        [ReadOnly] public EntityTypeHandle EntityH;
        [ReadOnly] public ComponentTypeHandle<SpikeId> IdH;
        [ReadOnly] public ComponentTypeHandle<SpikeHealth> HealthH;
        [ReadOnly] public ComponentTypeHandle<SpikeTarget> TargetH;
        [ReadOnly] public ComponentTypeHandle<SpikeStunned> StunnedH;
        [ReadOnly] public BufferTypeHandle<SpikeInventory> InvH;
        [ReadOnly] public ComponentTypeHandle<SpikePosition> PosH;
        [ReadOnly] public ComponentTypeHandle<SpikePlayer> PlayerH;

        [NativeDisableParallelForRestriction] public NativeArray<Entity> Entities;
        [NativeDisableParallelForRestriction] public NativeArray<byte> Flags;
        [NativeDisableParallelForRestriction] public NativeArray<int> Ids;
        [NativeDisableParallelForRestriction] public NativeArray<int2> Health;
        [NativeDisableParallelForRestriction] public NativeArray<Entity> TargetEntity;
        [NativeDisableParallelForRestriction] public NativeArray<int> TargetIdx;
        [NativeDisableParallelForRestriction] public NativeArray<int> InvOffset;
        [NativeDisableParallelForRestriction] public NativeArray<float3> Pos;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            int b = BaseIndex[unfilteredChunkIndex];
            int n = chunk.Count;
            var ents = chunk.GetNativeArray(EntityH);

            bool hId = chunk.Has(ref IdH);
            bool hHealth = chunk.Has(ref HealthH);
            bool hTarget = chunk.Has(ref TargetH);
            bool hStun = chunk.Has(ref StunnedH);
            bool hInv = chunk.Has(ref InvH);
            bool hPos = chunk.Has(ref PosH);
            bool hPlayer = chunk.Has(ref PlayerH);

            var ids = hId ? chunk.GetNativeArray(ref IdH) : default;
            var hp = hHealth ? chunk.GetNativeArray(ref HealthH) : default;
            var tg = hTarget ? chunk.GetNativeArray(ref TargetH) : default;
            var ps = hPos ? chunk.GetNativeArray(ref PosH) : default;
            var inv = hInv ? chunk.GetBufferAccessor(ref InvH) : default;

            for (int i = 0; i < n; i++)
            {
                int g = b + i;
                byte f = 0;
                Entities[g] = ents[i];

                if (hId) { f |= SpikeFlags.HasId; Ids[g] = ids[i].Value; }
                else Ids[g] = SpikeLayout.NoId;

                if (hHealth) { f |= SpikeFlags.HasHealth; Health[g] = new int2(hp[i].Current, hp[i].Max); }
                else Health[g] = default;

                if (hTarget) { f |= SpikeFlags.HasTarget; TargetEntity[g] = tg[i].Value; }
                else TargetEntity[g] = Entity.Null;

                if (hStun)
                {
                    f |= SpikeFlags.HasStunned;
                    if (chunk.IsComponentEnabled(ref StunnedH, i)) f |= SpikeFlags.StunnedEnabled;
                }

                if (hInv) { f |= SpikeFlags.HasInventory; InvOffset[g] = math.min(inv[i].Length, SpikeLayout.MaxInvPerEntity); }
                else InvOffset[g] = 0;

                if (hPos) { f |= SpikeFlags.HasPosition; Pos[g] = ps[i].Value; }
                else Pos[g] = default;

                if (hPlayer) f |= SpikeFlags.IsPlayer;

                Flags[g] = f;
                TargetIdx[g] = -1;
            }
        }
    }

    [BurstCompile]
    struct SnapshotInvJob : IJobChunk
    {
        [ReadOnly] public NativeArray<int> BaseIndex;
        [ReadOnly] public BufferTypeHandle<SpikeInventory> InvH;
        [ReadOnly] public NativeArray<int> InvOffset;
        [NativeDisableParallelForRestriction] public NativeArray<int2> InvElems;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex,
            bool useEnabledMask, in v128 chunkEnabledMask)
        {
            if (!chunk.Has(ref InvH)) return;
            int b = BaseIndex[unfilteredChunkIndex];
            var acc = chunk.GetBufferAccessor(ref InvH);
            for (int i = 0; i < chunk.Count; i++)
            {
                var buf = acc[i];
                int off = InvOffset[b + i];
                int len = math.min(buf.Length, SpikeLayout.MaxInvPerEntity);
                for (int k = 0; k < len; k++)
                    InvElems[off + k] = new int2(buf[k].ItemId, buf[k].Count);
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GameWriterSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            if (!SpikeToggles.GameWriterEnabled) return;

            if (SpikeToggles.MainThreadProbe && state.EntityManager.Exists(SpikeToggles.ProbeEntity))
            {
                var sw = Stopwatch.StartNew();
                state.EntityManager.SetComponentData(
                    SpikeToggles.ProbeEntity, new SpikeHealth { Current = 1, Max = 1 });
                SpikeToggles.ProbeWaitTicks = sw.ElapsedTicks;
            }

            state.Dependency = new WriteHealthJob().ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct WriteHealthJob : IJobEntity
    {
        void Execute(ref SpikeHealth h)
        {
            h.Current = (h.Current + 1) & 0xFFFF;
        }
    }
}
