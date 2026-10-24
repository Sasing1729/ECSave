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
            // 初回呼び出し時は「呼び出し元スレッドを main thread として記録」する。
            // Unity の Awake/Start/Update は main thread で走るため、
            // SaveSystem の初回呼び出し元は main thread であることが期待される。
            // ここで自動記録しないと、SaveRuntimeSystem.OnCreate より前に
            // SaveSystem を呼んだ場合に、main thread から呼んでいても例外が飛ぶ。
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

        internal static void ResetForTests()
        {
            _recordedMainThreadId = 0;
            _recorded = false;
        }
    }
}
