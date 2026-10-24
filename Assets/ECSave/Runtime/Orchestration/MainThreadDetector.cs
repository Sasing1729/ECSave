using System.Threading;
using UnityEngine;

namespace ECSave.Internal
{
    public static class MainThreadDetector
    {
        private static int _recordedMainThreadId;
        private static bool _recorded;

        public static void RecordMainThreadId()
        {
            if (_recorded) return;
            _recordedMainThreadId = Thread.CurrentThread.ManagedThreadId;
            _recorded = true;
        }

        public static void EnsureRecorded()
        {
            if (!_recorded) RecordMainThreadId();
        }

        public static bool IsMainThread()
        {
            if (!_recorded) return false;
            return Thread.CurrentThread.ManagedThreadId == _recordedMainThreadId;
        }

        public static void ThrowIfNotMainThread()
        {
            if (!_recorded)
            {
                RecordMainThreadId();
                return;
            }

            if (!IsMainThread())
            {
                throw new System.InvalidOperationException(
                    $"ECSave public API must be called from the main thread. " +
                    $"RecordedMainThreadId={_recordedMainThreadId}, " +
                    $"CurrentThreadId={Thread.CurrentThread.ManagedThreadId}.");
            }
        }

        /// <summary>
        /// Review #11: Domain Reload 無効時の静的状態リセット。
        /// セッション間で古い main thread ID が残ると、別スレッド ID の
        /// 新セッションで ThrowIfNotMainThread が誤検出する。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(
            RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void ResetOnLoad()
        {
            _recordedMainThreadId = 0;
            _recorded = false;
        }

        internal static void ResetForTests()
        {
            _recordedMainThreadId = 0;
            _recorded = false;
        }
    }
}
