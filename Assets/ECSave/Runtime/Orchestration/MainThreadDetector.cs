// Runtime/Orchestration/MainThreadDetector.cs
using System.Threading;
using UnityEngine;

namespace ECSave.Internal
{
    /// <summary>
    /// メインスレッド判定（14.2節、A-11）。
    /// ECS 内部は Worker からの EntityManager 操作を必ずしも拒否しないため、
    /// ECSave が自前で判定する。
    /// </summary>
    public static class MainThreadDetector
    {
        private static int _recordedMainThreadId;
        private static bool _recorded;

        /// <summary>起動時に 1 回だけ記録する。SaveRuntimeSystem.OnCreate から呼ぶ。</summary>
        public static void RecordMainThreadId()
        {
            if (_recorded) return;
            _recordedMainThreadId = Thread.CurrentThread.ManagedThreadId;
            _recorded = true;
        }

        /// <summary>記録がまだ無い場合に、その場で記録する（フォールバック）。</summary>
        public static void EnsureRecorded()
        {
            if (!_recorded) RecordMainThreadId();
        }

        public static bool IsMainThread()
        {
            if (!_recorded) return false;
            return Thread.CurrentThread.ManagedThreadId == _recordedMainThreadId;
        }

        /// <summary>公開 API の入口で呼ぶ。メインスレッドでなければ同期スロー（14.2節）。</summary>
        public static void ThrowIfNotMainThread()
        {
            if (!IsMainThread())
                throw new System.InvalidOperationException(
                    $"ECSave public API must be called from the main thread. " +
                    $"Recorded={_recordedMainThreadId}, Current={Thread.CurrentThread.ManagedThreadId}");
        }

        /// <summary>テスト用に記録をリセットする。プロダクションでは使わない。</summary>
        internal static void ResetForTests()
        {
            _recordedMainThreadId = 0;
            _recorded = false;
        }
    }
}
