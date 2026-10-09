using System.Collections.Generic;
using System.Diagnostics;

namespace ECSave.Internal
{
    public static class SerializeDiag
    {
        public static bool Enabled;

        private static readonly Stopwatch s_sw = new Stopwatch();
        private static readonly Dictionary<string, double> s_accumulated
            = new Dictionary<string, double>();

        public static void BeginPhase()
        {
            if (!Enabled) return;
            s_sw.Restart();
        }

        public static void EndPhase(string name)
        {
            if (!Enabled) return;
            s_sw.Stop();
            s_accumulated.TryGetValue(name, out double prev);
            s_accumulated[name] = prev + s_sw.Elapsed.TotalMilliseconds;
        }

        public static void Reset()
        {
            s_accumulated.Clear();
        }

        public static void Dump(string label, int iterations)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[SerializeDiag] {label} ({iterations} iterations)");
            foreach (var kv in s_accumulated)
                sb.AppendLine($"  {kv.Key}: {kv.Value / iterations:F3} ms");
            UnityEngine.Debug.Log(sb.ToString());
        }
    }
}
