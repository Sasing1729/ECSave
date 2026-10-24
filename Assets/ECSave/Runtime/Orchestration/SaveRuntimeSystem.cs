using Unity.Entities;

namespace ECSave.Internal
{
    [UpdateInGroup(typeof(ECSaveSnapshotGroup))]
    public partial class SaveRuntimeSystem : SystemBase
    {
        private WorldOperationGuard _guard;
        private Operation _current;

        public Operation Current => _current;
        public bool IsBusy => _guard != null && _guard.IsHeld;

        protected override void OnCreate()
        {
            _guard = new WorldOperationGuard();
            MainThreadDetector.RecordMainThreadId();
        }

        protected override void OnUpdate()
        {
            var op = _current;
            if (op == null) return;
            if (op.IsComplete) return;

            try
            {
                op.MoveNext();
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[ECSave] SaveRuntimeSystem.OnUpdate threw: {ex}");
            }

#if DEBUG
            try
            {
                if (op.IsComplete)
                {
                    UnityEngine.Debug.Log(
                        $"[ECSave] {op.Kind} completed, finalPhase={op.Phase}");
                }
                else
                {
                    UnityEngine.Debug.Log(
                        $"[ECSave] {op.Kind} → {op.Phase}");
                }
            }
            catch
            {
            }
#endif
        }

        protected override void OnDestroy()
        {
            var op = _current;
            if (op != null && !op.IsComplete)
            {
                op.MarkWorldDestroyed();

                var code = op.Kind == OperationKind.Serialize
                    ? SaveLoadErrorCode.SerializationFailed
                    : SaveLoadErrorCode.WorldModificationFailed;

                try { op.OnAbort?.Invoke(code); }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError(
                        $"[ECSave] Operation.OnAbort threw: {ex}");
                }

                op.MarkComplete();
            }
            _current = null;

            try { _guard?.Release(); } catch { }
        }

        internal void ForceCleanupForTests()
        {
            OnDestroy();
        }

        public bool TryAcquireOperation()
        {
            if (_guard == null) _guard = new WorldOperationGuard();
            return _guard.TryAcquire();
        }

        public void ReleaseOperation()
        {
            try { _guard?.Release(); } catch { }
        }

        public void SetCurrent(Operation op) => _current = op;
    }
}
