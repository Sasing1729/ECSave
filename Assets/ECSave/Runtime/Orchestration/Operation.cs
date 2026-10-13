using System.Threading;

namespace ECSave.Internal
{
    public enum OperationKind : byte
    {
        Serialize = 0,
        Deserialize = 1,
    }

    public sealed class Operation
    {
        public OperationKind Kind { get; }
        public OperationPhase Phase { get; private set; }
        public ErrorInfo PendingError { get; private set; }
        public CancellationToken CancellationToken { get; }
        public bool IsComplete { get; private set; }
        public bool WorldDestroyed { get; private set; }

        public Operation(OperationKind kind, CancellationToken ct)
        {
            Kind = kind;
            CancellationToken = ct;
            Phase = kind == OperationKind.Serialize
                ? OperationPhase.S0_Guard
                : OperationPhase.D0_Guard;
        }

        public void SetPhase(OperationPhase phase)
        {
            Phase = phase;
        }

        public void SetPendingError(in ErrorInfo error)
        {
            if (PendingError.IsNone)
                PendingError = error;
        }

        public void MarkComplete()
        {
            IsComplete = true;
        }

        public void MarkWorldDestroyed()
        {
            WorldDestroyed = true;
            MarkComplete();
        }
    }
}
