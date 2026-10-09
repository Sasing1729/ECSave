using ECSave.Tests;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Debug = UnityEngine.Debug;

namespace ECSave.Tests
{
    public struct Big16 { public long A; public long B; }

    /// <summary>
    /// A-17: default(T).Capacity が Burst ジョブ内で呼べること。
    ///       FixedList.PaddingBytes&lt;T&gt;() が T のアラインメントから算出できること。
    /// 確認: (1) Burst 内の Capacity が managed と一致 (2) 容量が定数でなく型から算出されること
    ///       (3) 式 (Size-2-pad)/sizeof(T) との一致 (4) PaddingBytes の可視性
    ///       (5) FixedList*Bytes&lt;Entity&gt; のデータ領域の開始オフセット（SchemaHash 入力列の「参照領域」）
    /// </summary>
    public unsafe class A17_FixedCapacityTests
    {
        const int Count = 30;

        static readonly string[] Names = {
            "FixedString32Bytes", "FixedString64Bytes", "FixedString128Bytes", "FixedString512Bytes", "FixedString4096Bytes", "FixedList32Bytes<byte>", "FixedList64Bytes<byte>", "FixedList128Bytes<byte>", "FixedList512Bytes<byte>", "FixedList4096Bytes<byte>", "FixedList32Bytes<int>", "FixedList64Bytes<int>", "FixedList128Bytes<int>", "FixedList512Bytes<int>", "FixedList4096Bytes<int>", "FixedList32Bytes<Entity>", "FixedList64Bytes<Entity>", "FixedList128Bytes<Entity>", "FixedList512Bytes<Entity>", "FixedList4096Bytes<Entity>", "FixedList32Bytes<double>", "FixedList64Bytes<double>", "FixedList128Bytes<double>", "FixedList512Bytes<double>", "FixedList4096Bytes<double>", "FixedList32Bytes<Big16>", "FixedList64Bytes<Big16>", "FixedList128Bytes<Big16>", "FixedList512Bytes<Big16>", "FixedList4096Bytes<Big16>"
        };

        static void Fill(int* o)
        {
            int i = 0;
            o[i++] = default(FixedString32Bytes).Capacity;
            o[i++] = default(FixedString64Bytes).Capacity;
            o[i++] = default(FixedString128Bytes).Capacity;
            o[i++] = default(FixedString512Bytes).Capacity;
            o[i++] = default(FixedString4096Bytes).Capacity;
            o[i++] = default(FixedList32Bytes<byte>).Capacity;
            o[i++] = default(FixedList64Bytes<byte>).Capacity;
            o[i++] = default(FixedList128Bytes<byte>).Capacity;
            o[i++] = default(FixedList512Bytes<byte>).Capacity;
            o[i++] = default(FixedList4096Bytes<byte>).Capacity;
            o[i++] = default(FixedList32Bytes<int>).Capacity;
            o[i++] = default(FixedList64Bytes<int>).Capacity;
            o[i++] = default(FixedList128Bytes<int>).Capacity;
            o[i++] = default(FixedList512Bytes<int>).Capacity;
            o[i++] = default(FixedList4096Bytes<int>).Capacity;
            o[i++] = default(FixedList32Bytes<Entity>).Capacity;
            o[i++] = default(FixedList64Bytes<Entity>).Capacity;
            o[i++] = default(FixedList128Bytes<Entity>).Capacity;
            o[i++] = default(FixedList512Bytes<Entity>).Capacity;
            o[i++] = default(FixedList4096Bytes<Entity>).Capacity;
            o[i++] = default(FixedList32Bytes<double>).Capacity;
            o[i++] = default(FixedList64Bytes<double>).Capacity;
            o[i++] = default(FixedList128Bytes<double>).Capacity;
            o[i++] = default(FixedList512Bytes<double>).Capacity;
            o[i++] = default(FixedList4096Bytes<double>).Capacity;
            o[i++] = default(FixedList32Bytes<Big16>).Capacity;
            o[i++] = default(FixedList64Bytes<Big16>).Capacity;
            o[i++] = default(FixedList128Bytes<Big16>).Capacity;
            o[i++] = default(FixedList512Bytes<Big16>).Capacity;
            o[i++] = default(FixedList4096Bytes<Big16>).Capacity;
        }

        [BurstCompile(CompileSynchronously = true)]
        struct CapacityJob : IJob
        {
            public NativeArray<int> Out;      // Count 個
            public NativeArray<int> WasManaged;

            public void Execute()
            {
                Fill((int*)Out.GetUnsafePtr());
                WasManaged[0] = BurstProbe.IsManaged() ? 1 : 0;
            }
        }

        static MethodInfo PaddingMethod()
        {
            var t = typeof(FixedList128Bytes<int>).Assembly.GetType("Unity.Collections.FixedList");
            return t?.GetMethod("PaddingBytes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }

        // Collections 内部の PaddingBytes<T>() の実値（非 public のためリフレクション。テスト専用）
        static int PadOf<T>() where T : unmanaged
        {
            var m = PaddingMethod();
            if (m == null) Assert.Inconclusive("FixedList.PaddingBytes<T>() が見つからない");
            return (int)m.MakeGenericMethod(typeof(T)).Invoke(null, null);
        }

        static int Calc(int totalSize, int elemSize, int pad)
        {
            return (totalSize - 2 - pad) / elemSize;
        }

        static Dictionary<string, int> Expected()
        {
            var d = new Dictionary<string, int>();
            d["FixedString32Bytes"] = 29;
            d["FixedString64Bytes"] = 61;
            d["FixedString128Bytes"] = 125;
            d["FixedString512Bytes"] = 509;
            d["FixedString4096Bytes"] = 4093;
            d["FixedList32Bytes<byte>"] = Calc(32, UnsafeUtility.SizeOf<byte>(), PadOf<byte>());
            d["FixedList64Bytes<byte>"] = Calc(64, UnsafeUtility.SizeOf<byte>(), PadOf<byte>());
            d["FixedList128Bytes<byte>"] = Calc(128, UnsafeUtility.SizeOf<byte>(), PadOf<byte>());
            d["FixedList512Bytes<byte>"] = Calc(512, UnsafeUtility.SizeOf<byte>(), PadOf<byte>());
            d["FixedList4096Bytes<byte>"] = Calc(4096, UnsafeUtility.SizeOf<byte>(), PadOf<byte>());
            d["FixedList32Bytes<int>"] = Calc(32, UnsafeUtility.SizeOf<int>(), PadOf<int>());
            d["FixedList64Bytes<int>"] = Calc(64, UnsafeUtility.SizeOf<int>(), PadOf<int>());
            d["FixedList128Bytes<int>"] = Calc(128, UnsafeUtility.SizeOf<int>(), PadOf<int>());
            d["FixedList512Bytes<int>"] = Calc(512, UnsafeUtility.SizeOf<int>(), PadOf<int>());
            d["FixedList4096Bytes<int>"] = Calc(4096, UnsafeUtility.SizeOf<int>(), PadOf<int>());
            d["FixedList32Bytes<Entity>"] = Calc(32, UnsafeUtility.SizeOf<Entity>(), PadOf<Entity>());
            d["FixedList64Bytes<Entity>"] = Calc(64, UnsafeUtility.SizeOf<Entity>(), PadOf<Entity>());
            d["FixedList128Bytes<Entity>"] = Calc(128, UnsafeUtility.SizeOf<Entity>(), PadOf<Entity>());
            d["FixedList512Bytes<Entity>"] = Calc(512, UnsafeUtility.SizeOf<Entity>(), PadOf<Entity>());
            d["FixedList4096Bytes<Entity>"] = Calc(4096, UnsafeUtility.SizeOf<Entity>(), PadOf<Entity>());
            d["FixedList32Bytes<double>"] = Calc(32, UnsafeUtility.SizeOf<double>(), PadOf<double>());
            d["FixedList64Bytes<double>"] = Calc(64, UnsafeUtility.SizeOf<double>(), PadOf<double>());
            d["FixedList128Bytes<double>"] = Calc(128, UnsafeUtility.SizeOf<double>(), PadOf<double>());
            d["FixedList512Bytes<double>"] = Calc(512, UnsafeUtility.SizeOf<double>(), PadOf<double>());
            d["FixedList4096Bytes<double>"] = Calc(4096, UnsafeUtility.SizeOf<double>(), PadOf<double>());
            d["FixedList32Bytes<Big16>"] = Calc(32, UnsafeUtility.SizeOf<Big16>(), PadOf<Big16>());
            d["FixedList64Bytes<Big16>"] = Calc(64, UnsafeUtility.SizeOf<Big16>(), PadOf<Big16>());
            d["FixedList128Bytes<Big16>"] = Calc(128, UnsafeUtility.SizeOf<Big16>(), PadOf<Big16>());
            d["FixedList512Bytes<Big16>"] = Calc(512, UnsafeUtility.SizeOf<Big16>(), PadOf<Big16>());
            d["FixedList4096Bytes<Big16>"] = Calc(4096, UnsafeUtility.SizeOf<Big16>(), PadOf<Big16>());
            return d;
        }

        [Test]
        public void Capacity_InBurstJob_EqualsManaged()
        {
            var managed = new int[Count];
            fixed (int* p = managed) Fill(p);

            var o = new NativeArray<int>(Count, Allocator.TempJob);
            var w = new NativeArray<int>(1, Allocator.TempJob);
            try
            {
                new CapacityJob { Out = o, WasManaged = w }.Run();
                Debug.Log($"[A-17] job executed as managed={w[0] == 1} (BurstCompiler.IsEnabled={BurstCompiler.IsEnabled})");
                for (int i = 0; i < Count; i++)
                    Assert.AreEqual(managed[i], o[i], $"{Names[i]}: Burst={o[i]} managed={managed[i]}");
                if (w[0] == 1)
                    Assert.Inconclusive("Burst で実行されていない（Jobs > Burst > Enable Compilation を有効にして再実行）。A-17 の Burst 互換は未検証");
            }
            finally { o.Dispose(); w.Dispose(); }
        }

        [Test]
        public void Capacity_MatchesFormula_FromSizeAndAlignment()
        {
            var actual = new int[Count];
            fixed (int* p = actual) Fill(p);
            var exp = Expected();
            var mismatches = new List<string>();
            for (int i = 0; i < Count; i++)
            {
                int e = exp[Names[i]];
                Debug.Log($"[A-17] {Names[i]} capacity={actual[i]} expected={e}");
                if (e != actual[i]) mismatches.Add($"{Names[i]}: actual={actual[i]} expected={e}");
            }
            Assert.IsEmpty(mismatches,
                "式 (Size-2-pad)/sizeof(T), pad=max(0,min(CacheLine,align)-2) と一致しない型がある。" +
                "SchemaHash 入力列からの Capacity 導出（ADR-29）の前提を見直すこと。\n" + string.Join("\n", mismatches));
        }

        [Test]
        public void Capacity_DependsOnElementType_NotConstant()
        {
            // 型で容量が変わる＝コンパイル時定数でないことの確認（ADR-29 の根拠）
            Assert.AreNotEqual(default(FixedList128Bytes<byte>).Capacity, default(FixedList128Bytes<Entity>).Capacity);
            Assert.AreNotEqual(default(FixedList128Bytes<int>).Capacity, default(FixedList128Bytes<Big16>).Capacity);
        }

        [Test]
        public void PaddingBytes_Visibility_LogOnly()
        {
            var t = typeof(FixedList128Bytes<int>).Assembly.GetType("Unity.Collections.FixedList");
            if (t == null) { Debug.Log("[A-17] 型 Unity.Collections.FixedList が見つからない"); Assert.Inconclusive(); return; }
            var m = t.GetMethod("PaddingBytes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) { Debug.Log("[A-17] PaddingBytes<T>() が見つからない。自前の式で代替する"); Assert.Inconclusive(); return; }
            Debug.Log($"[A-17] PaddingBytes<T>() 発見: public={m.IsPublic}");
            foreach (var elem in new[] { typeof(byte), typeof(int), typeof(Entity), typeof(double), typeof(Big16) })
            {
                int pad = (int)m.MakeGenericMethod(elem).Invoke(null, null);
                Debug.Log($"[A-17] PaddingBytes<{elem.Name}>={pad} (sizeof={UnsafeUtility.SizeOf(elem)})");
            }
            Assert.Pass();
        }

        static int FindOffset<TList>(ref TList list, Entity marker) where TList : unmanaged
        {
            byte* basePtr = (byte*)UnsafeUtility.AddressOf(ref list);
            int size = UnsafeUtility.SizeOf<TList>();
            for (int off = 0; off + 8 <= size; off++)
                if (UnsafeUtility.MemCmp(basePtr + off, &marker, 8) == 0) return off;
            return -1;
        }

        [Test]
        public void FixedListOfEntity_DataRegionOffset()
        {
            var marker = new Entity { Index = 0x01020304, Version = 0x05060708 };
            int pad = PadOf<Entity>();

            var l32 = new FixedList32Bytes<Entity>(); l32.Add(marker);
            var l64 = new FixedList64Bytes<Entity>(); l64.Add(marker);
            var l128 = new FixedList128Bytes<Entity>(); l128.Add(marker);
            var l512 = new FixedList512Bytes<Entity>(); l512.Add(marker);
            var l4096 = new FixedList4096Bytes<Entity>(); l4096.Add(marker);

            int[] offs = { FindOffset(ref l32, marker), FindOffset(ref l64, marker), FindOffset(ref l128, marker),
                           FindOffset(ref l512, marker), FindOffset(ref l4096, marker) };
            string offsText = string.Join(",", offs);
            Debug.Log($"[A-17] FixedList*Bytes<Entity> data offset: {offsText} (expected 2+pad={2 + pad})");
            foreach (var o in offs) Assert.AreEqual(2 + pad, o, "データ領域の開始位置が想定（長さ u16 + パディング）と異なる");
        }


        public struct Byte3 { public byte A, B, C; }
        public struct Short1 { public ushort A; }
        public struct Triple { public int A, B, C; }
        public struct Quad { public int A, B, C, D; }
        public struct Five { public int A, B, C, D, E; }          // 20 bytes
        public struct Six { public int A, B, C, D, E, F; }        // 24 bytes
        public struct Ush3 { public ushort A, B, C; }             // 6 bytes
        public struct Ush5 { public ushort A, B, C, D, E; }       // 10 bytes

        static void LogPad<T>() where T : unmanaged
        {
            int size = UnsafeUtility.SizeOf<T>(), align = UnsafeUtility.AlignOf<T>(), pad = PadOf<T>();
            int byAlign = math.max(0, math.min(8, align) - 2);
            int bySize = math.max(0, math.min(8, size) - 2);
            int lowBit = size & -size; // sizeof を割り切る最大の 2 のべき乗
            int byLowBit = math.max(0, math.min(8, lowBit) - 2);
            Debug.Log($"[A-17] PaddingBytes<{typeof(T).Name}>={pad} sizeof={size} alignof={align} | alignベース={byAlign}{(byAlign == pad ? "○" : "×")} sizeベース(上限8)={bySize}{(bySize == pad ? "○" : "×")} sizeの最下位ビット={byLowBit}{(byLowBit == pad ? "○" : "×")}");
        }

        [Test]
        public void PaddingBytes_CandidateFormulas_LogOnly()
        {
            LogPad<byte>(); LogPad<Byte3>(); LogPad<Short1>(); LogPad<int>(); LogPad<Triple>(); LogPad<Quad>();
            LogPad<Entity>(); LogPad<double>(); LogPad<Big16>();
            LogPad<Five>(); LogPad<Six>(); LogPad<Ush3>(); LogPad<Ush5>();
            Assert.Pass();
        }

        [Test]
        public void StructSizes_AreAsNamed()
        {
            Assert.AreEqual(32, UnsafeUtility.SizeOf<FixedList32Bytes<int>>());
            Assert.AreEqual(64, UnsafeUtility.SizeOf<FixedList64Bytes<int>>());
            Assert.AreEqual(128, UnsafeUtility.SizeOf<FixedList128Bytes<int>>());
            Assert.AreEqual(512, UnsafeUtility.SizeOf<FixedList512Bytes<int>>());
            Assert.AreEqual(4096, UnsafeUtility.SizeOf<FixedList4096Bytes<int>>());
        }
    }
}
