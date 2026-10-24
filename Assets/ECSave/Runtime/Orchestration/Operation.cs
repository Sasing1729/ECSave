using System;
using System.Threading;

namespace ECSave.Internal
{
    public enum OperationKind : byte
    {
        Serialize = 0,
        Deserialize = 1,
    }

    public enum OperationState : byte
    {
        Pending = 0,
        Running = 1,
        Completed = 2,
    }

    public sealed class Operation
    {
        public OperationKind Kind { get; }
        public CancellationToken CancellationToken { get; }

        public OperationState State { get; private set; }
        public OperationPhase Phase { get; private set; }
        public ErrorInfo PendingError { get; private set; }
        public bool HasPendingError { get; private set; }
        public bool WorldDestroyed { get; private set; }

        public bool IsComplete => State == OperationState.Completed;

        private Func<Operation, bool> _drive;

        public Action<Operation> OnComplete { get; set; }
        public Action<SaveLoadErrorCode> OnAbort { get; set; }

        public Operation(
            OperationKind kind,
            CancellationToken cancellationToken,
            Func<Operation, bool> drive = null)
        {
            Kind = kind;
            CancellationToken = cancellationToken;
            _drive = drive;
            State = OperationState.Pending;

            // 既存互換: 構築直後の初期 Phase を Kind に応じて設定
            Phase = kind == OperationKind.Serialize
                ? OperationPhase.S0_Guard
                : OperationPhase.D0_Guard;
        }

        public void SetDrive(Func<Operation, bool> drive) => _drive = drive;

        public bool MoveNext()
        {
            if (State == OperationState.Completed) return true;
            if (State == OperationState.Pending) State = OperationState.Running;

            bool done;
            try
            {
                done = _drive != null && _drive(this);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[ECSave] Operation drive threw ({Kind}): {ex}");

                // 駆動関数が投げた例外を「エラー発生」として記録する。
                // ここでは具体的な SaveLoadErrorCode が不明なため ErrorInfo.None
                // （Code = None）を暫定値として置く。実際の結果は
                // SerializeOperation / DeserializeOperation 側が
                // SaveResult.Failure / LoadResult.Failure として返す。
                //
                // この PendingError は将来、操作の失敗理由を
                // Operation 経由で集約する用途のために予約されている。
                // 現状は HasPendingError = true の記録のみが目的。
                SetPendingError(ErrorInfo.None);
                done = true;
            }

            if (done)
            {
                State = OperationState.Completed;

                try { OnComplete?.Invoke(this); }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"[ECSave] Operation.OnComplete threw ({Kind}): {ex}");
                }
            }
            return done;
        }

        public void SetPhase(OperationPhase phase) => Phase = phase;

        public void SetPendingError(in ErrorInfo error)
        {
            if (HasPendingError) return;
            PendingError = error;
            HasPendingError = true;
        }

        public void MarkComplete() => State = OperationState.Completed;

        /// <summary>
        /// World 破棄時に呼ぶ。WorldDestroyed フラグと完了状態を両方立てる。
        /// </summary>
        public void MarkWorldDestroyed()
        {
            WorldDestroyed = true;
            State = OperationState.Completed;
        }
    }
}
