using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ECSave.Tests.Generator
{
    public struct ProbeInnerStruct
    {
        public int Score;
        public FixedString32Bytes Label;
    }

    public struct ProbeInnerStructNoInt
    {
        public FixedString32Bytes Label;
    }

    public struct ProbeInnerStructInt64
    {
        public long Score;
        public FixedString32Bytes Label;
    }

    /// <summary>
    /// Unity.Collections.FixedList の内部実装を反射で調査する診断用プローブ。CI からは除外する。
    /// </summary>
    [TestFixture]
    [Category("Verification")]
    public sealed class FixedListPaddingProbe
    {
        [Test]
        public void Probe01_PaddingBytes_AllTypes()
        {
            var assembly = typeof(FixedList128Bytes<FixedString32Bytes>).Assembly;
            var fixedListType = assembly.GetType("Unity.Collections.FixedList");

            Assert.IsNotNull(fixedListType);
            Debug.Log($"[PROBE01] Unity.Collections.FixedList found in: {assembly.GetName().Name}");

            var method = fixedListType.GetMethod("PaddingBytes",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            Debug.Log($"[PROBE01] PaddingBytes visibility: IsPublic={method.IsPublic}, IsStatic={method.IsStatic}");

            void Report<T>(string name) where T : unmanaged
            {
                int pad = (int)method.MakeGenericMethod(typeof(T)).Invoke(null, null);
                int size = UnsafeUtility.SizeOf<T>();
                int align = UnsafeUtility.AlignOf<T>();
                int sizeFormula = System.Math.Max(0, System.Math.Min(8, size) - 2);
                int alignFormula = System.Math.Max(0, System.Math.Min(8, align) - 2);
                Debug.Log($"[PROBE01] {name}: PaddingBytes={pad}, SizeOf={size}, AlignOf={align}, " +
                          $"sizeFormula={sizeFormula}, alignFormula={alignFormula}, " +
                          $"matchSize={(pad == sizeFormula ? "YES" : "NO")}, matchAlign={(pad == alignFormula ? "YES" : "NO")}");
            }

            Report<FixedString32Bytes>("FixedString32Bytes");
            Report<FixedString64Bytes>("FixedString64Bytes");
            Report<int>("int");
            Report<byte>("byte");
            Report<long>("long");
            Report<Unity.Entities.Entity>("Entity");
            Report<ProbeInnerStruct>("ProbeInnerStruct(int + FixedString32)");
            Report<ProbeInnerStructNoInt>("ProbeInnerStructNoInt(FixedString32)");
            Report<ProbeInnerStructInt64>("ProbeInnerStructInt64(long + FixedString32)");

            Assert.Pass();
        }

        [Test]
        public unsafe void Probe05_FixedListOfStruct_Layout()
        {
            var list = new FixedList512Bytes<ProbeInnerStruct>();
            list.Add(new ProbeInnerStruct { Score = 10, Label = "ABCDEFGH" });
            list.Add(new ProbeInnerStruct { Score = 20, Label = "IJKLMNOP" });

            byte* basePtr = (byte*)UnsafeUtility.AddressOf(ref list);
            int totalSize = UnsafeUtility.SizeOf<FixedList512Bytes<ProbeInnerStruct>>();
            int structSize = UnsafeUtility.SizeOf<ProbeInnerStruct>();

            var sb = new StringBuilder();
            sb.AppendLine($"[PROBE05] FixedList512Bytes<ProbeInnerStruct> size={totalSize}, structSize={structSize}");
            sb.AppendLine("[PROBE05] Hex dump (first 128 bytes):");
            for (int row = 0; row < 128 && row < totalSize; row += 16)
            {
                sb.Append($"  [{row:D3}] ");
                for (int i = 0; i < 16 && row + i < totalSize; i++)
                    sb.Append($"{basePtr[row + i]:X2} ");
                sb.AppendLine();
            }

            int FindAscii(byte* ptr, int total, string needle)
            {
                var bytes = Encoding.ASCII.GetBytes(needle);
                for (int i = 0; i + bytes.Length <= total; i++)
                {
                    bool match = true;
                    for (int k = 0; k < bytes.Length; k++)
                    {
                        if (ptr[i + k] != bytes[k]) { match = false; break; }
                    }
                    if (match) return i;
                }
                return -1;
            }

            int found1 = FindAscii(basePtr, totalSize, "ABCDEFGH");
            int found2 = FindAscii(basePtr, totalSize, "IJKLMNOP");
            sb.AppendLine($"[PROBE05] 'ABCDEFGH' ASCII found at offset: {found1}");
            sb.AppendLine($"[PROBE05] 'IJKLMNOP' ASCII found at offset: {found2}");

            if (found1 >= 0)
            {
                int structOffsetInElem = (int)UnsafeUtility.SizeOf<int>();
                int elementDataOffset = found1 - 2 - structOffsetInElem;
                sb.AppendLine($"[PROBE05] ElementDataOffset (found1 - 2 - SizeOf<int>) = {elementDataOffset}");
            }

            if (found2 >= 0 && found1 >= 0)
            {
                int stride = found2 - found1;
                sb.AppendLine($"[PROBE05] ElementStride (found2 - found1) = {stride}");
            }

            var assembly = typeof(FixedList512Bytes<ProbeInnerStruct>).Assembly;
            var fixedListType = assembly.GetType("Unity.Collections.FixedList");
            var method = fixedListType.GetMethod("PaddingBytes",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            int pad = (int)method.MakeGenericMethod(typeof(ProbeInnerStruct)).Invoke(null, null);
            sb.AppendLine($"[PROBE05] PaddingBytes<ProbeInnerStruct> = {pad}");

            Debug.Log(sb.ToString());
            Assert.Pass();
        }
    }
}
