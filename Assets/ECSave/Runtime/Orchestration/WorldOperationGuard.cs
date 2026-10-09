// Runtime/Orchestration/WorldOperationGuard.cs
using System.Threading;

namespace ECSave.Internal
{
    /// <summary>
    /// World 単位の実行中フラグ（14.1節）。
    /// 公開 API はメインスレッドのみだが、World 単位で並行実行を検出するため
    /// Interlocked で安全側に倒す。
    /// </summary>
    public sealed class WorldOperationGuard
    {
        private int _inProgress;

        public bool IsHeld => Interlocked.CompareExchange(ref _inProgress, 0, 0) != 0;

        /// <summary>取得成功で true。既に実行中なら false（ConcurrentOperation）。</summary>
        public bool TryAcquire()
            => Interlocked.CompareExchange(ref _inProgress, 1, 0) == 0;

        public void Release()
            => Interlocked.Exchange(ref _inProgress, 0);
    }
}
