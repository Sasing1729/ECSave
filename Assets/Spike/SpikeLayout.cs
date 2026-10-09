using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace SaveSpike
{
    public enum SpikeError
    {
        None,
        CorruptedData,
        ChecksumMismatch,
        ValidationFailed,
    }

    /// <summary>Entityごとの「どの登録型を持っていたか」のビット。</summary>
    public static class SpikeFlags
    {
        public const byte HasId = 1 << 0;
        public const byte HasHealth = 1 << 1;
        public const byte HasTarget = 1 << 2;
        public const byte HasStunned = 1 << 3;
        public const byte StunnedEnabled = 1 << 4;   // 有効状態（アーキタイプには影響しない）
        public const byte HasInventory = 1 << 5;
        public const byte HasPosition = 1 << 6;
        public const byte IsPlayer = 1 << 7;
        public const byte ArchetypeMask = 0xEF;      // StunnedEnabled を除く
    }

    /// <summary>
    /// SaveData のバイナリレイアウト（内部設計書 4.1 節の縮小版）。
    /// 固定長数値はリトルエンディアン・パディングなし。構造体の直接メモリコピーはしない。
    ///
    /// [Header 40B] FormatVersion u32 | GameDataVersion u32 | SchemaHash u64 | WorldId 16B | BodyChecksum u64
    /// [Body]       Count i32 | InvTotal i32 | Fixed[Count * RecordSize] | InvElems[InvTotal * 8]
    /// Fixed record (33B): flags u8 | id i32 | hpCur i32 | hpMax i32 | targetIdx i32 | invCount i32 | pos 3*f32
    /// </summary>
    public static class SpikeLayout
    {
        public const uint FormatVersion = 1;
        public const uint GameDataVersion = 1;
        public const ulong SchemaHash = 0x53_50_49_4B_45_31UL;   // 仮値
        public const ulong WorldIdHi = 0x1111_2222_3333_4444UL; // 仮値
        public const ulong WorldIdLo = 0x5555_6666_7777_8888UL; // 仮値

        public const int HeaderSize = 4 + 4 + 8 + 16 + 8;        // 40
        public const int BodyPrefix = 8;
        public const int RecordSize = 33;
        public const int InvElemSize = 8;

        /// <summary>
        /// スパイク簡略化：Buffer要素数の上限を固定して配列を確保する。
        /// 本実装では「1パス目完了後にサイズ確定」か「過大確保」かの選択が要る（S1の考察対象）。
        /// </summary>
        public const int MaxInvPerEntity = 8;

        public const int NoId = int.MinValue;
    }

    /// <summary>手書きのリトルエンディアン読み書き。Burst内で使えるポインタ版。</summary>
    public static unsafe class Le
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteInt(byte* p, int v)
        {
            p[0] = (byte)v;
            p[1] = (byte)(v >> 8);
            p[2] = (byte)(v >> 16);
            p[3] = (byte)(v >> 24);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ReadInt(byte* p) => p[0] | (p[1] << 8) | (p[2] << 16) | (p[3] << 24);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteFloat(byte* p, float f) => WriteInt(p, math.asint(f));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ReadFloat(byte* p) => math.asfloat(ReadInt(p));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteULong(byte* p, ulong v)
        {
            WriteInt(p, (int)(uint)v);
            WriteInt(p + 4, (int)(uint)(v >> 32));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ReadULong(byte* p) => (uint)ReadInt(p) | ((ulong)(uint)ReadInt(p + 4) << 32);
    }

    /// <summary>
    /// S3 用：Burst コンパイルされていれば [BurstDiscard] メソッドは呼ばれない。
    /// 呼ばれた（＝managed で実行された）場合に true が立つ。
    /// </summary>
    public static class BurstProbe
    {
        [BurstDiscard]
        public static void MarkManaged(ref bool managed) { managed = true; }
    }

    /// <summary>
    /// 列指向のEntityデータ。スナップショット（Entities/TargetEntity あり）とデコード結果（なし）で共用する。
    /// </summary>
    public struct SpikeColumns : IDisposable
    {
        public int Count;
        public NativeArray<Entity> Entities;       // スナップショットのみ
        public NativeArray<byte> Flags;
        public NativeArray<int> Ids;               // 識別なしは SpikeLayout.NoId
        public NativeArray<int2> Health;           // (Current, Max)
        public NativeArray<Entity> TargetEntity;   // スナップショットのみ
        public NativeArray<int> TargetIdx;         // ファイル内インデックス。-1 = Entity.Null（Warningなし）／-2 = 保存対象外への参照（ロード時 Null + Warning）
        public NativeArray<int> InvOffset;         // Count + 1（排他的prefix sum）
        public NativeArray<int2> InvElems;         // (ItemId, Count)
        public NativeArray<float3> Pos;

        public static SpikeColumns Allocate(int count, int invElemCapacity, bool withEntities)
        {
            const NativeArrayOptions u = NativeArrayOptions.UninitializedMemory;
            var c = new SpikeColumns { Count = count };
            if (withEntities)
            {
                c.Entities = new NativeArray<Entity>(count, Allocator.Persistent, u);
                c.TargetEntity = new NativeArray<Entity>(count, Allocator.Persistent, u);
            }
            c.Flags = new NativeArray<byte>(count, Allocator.Persistent, u);
            c.Ids = new NativeArray<int>(count, Allocator.Persistent, u);
            c.Health = new NativeArray<int2>(count, Allocator.Persistent, u);
            c.TargetIdx = new NativeArray<int>(count, Allocator.Persistent, u);
            c.InvOffset = new NativeArray<int>(count + 1, Allocator.Persistent, u);
            c.InvElems = new NativeArray<int2>(math.max(1, invElemCapacity), Allocator.Persistent, u);
            c.Pos = new NativeArray<float3>(count, Allocator.Persistent, u);
            return c;
        }

        public void Dispose()
        {
            if (Entities.IsCreated) Entities.Dispose();
            if (TargetEntity.IsCreated) TargetEntity.Dispose();
            if (Flags.IsCreated) Flags.Dispose();
            if (Ids.IsCreated) Ids.Dispose();
            if (Health.IsCreated) Health.Dispose();
            if (TargetIdx.IsCreated) TargetIdx.Dispose();
            if (InvOffset.IsCreated) InvOffset.Dispose();
            if (InvElems.IsCreated) InvElems.Dispose();
            if (Pos.IsCreated) Pos.Dispose();
        }
    }

    /// <summary>InvOffset[0..Count-1] に入っている「要素数」を、排他的prefix sumに変換する。</summary>
    [BurstCompile]
    public struct PrefixJob : IJob
    {
        public int Count;
        public NativeArray<int> InvOffset;

        public void Execute()
        {
            int sum = 0;
            for (int i = 0; i < Count; i++)
            {
                int len = InvOffset[i];
                InvOffset[i] = sum;
                sum += len;
            }
            InvOffset[Count] = sum;
        }
    }
}
