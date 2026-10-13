using System.Threading;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave
{
    [UpdateInGroup(typeof(ECSaveSnapshotGroup))]
    public partial class SaveRuntimeSystem : SystemBase
    {
        private readonly WorldOperationGuard _guard = new();
        private Operation _current;

        public Operation Current => _current;
        public bool IsBusy => _guard.IsHeld;

        protected override void OnCreate()
        {
            MainThreadDetector.RecordMainThreadId();
        }

        protected override void OnUpdate()
        {
        }

        protected override void OnDestroy()
        {
            if (_current != null && !_current.IsComplete)
                _current.MarkWorldDestroyed();

            _guard.Release();
            _current = null;
        }

        public bool TryAcquireOperation() => _guard.TryAcquire();

        public void ReleaseOperation()
        {
            _guard.Release();
            _current = null;
        }

        public void SetCurrent(Operation op)
        {
            _current = op;
        }
    }
}
