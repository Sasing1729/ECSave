using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave
{
    public sealed class SaveSystem
    {
        private readonly SaveDefinition _definition;

        public SaveSystem(SaveDefinition definition)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public SaveDefinition Definition => _definition;

        // ── 同期実行（既存）────────────────────────

        public UniTask<SaveResult> SerializeAsync(
            World world,
            CancellationToken cancellationToken = default)
        {
            MainThreadDetector.ThrowIfNotMainThread();
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsCreated) throw new ObjectDisposedException(nameof(world));

            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.Cancelled));

            var sys = world.GetExistingSystemManaged<SaveRuntimeSystem>();
            if (sys != null && !sys.TryAcquireOperation())
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.ConcurrentOperation));

            try
            {
                var op = new Operation(OperationKind.Serialize, cancellationToken);
                if (sys != null) sys.SetCurrent(op);

                var inner = new SerializeOperation(_definition, world);

                op.SetPhase(OperationPhase.S1_AwaitSnapshot);
                var result = inner.Execute(cancellationToken);

                op.SetPhase(OperationPhase.S4_Complete);
                op.MarkComplete();

                return UniTask.FromResult(result);
            }
            catch (Format.FormatException fe)
            {
                return UniTask.FromResult(SaveResult.Failure(MapFormatError(fe.Error)));
            }
            catch
            {
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.SerializationFailed));
            }
            finally
            {
                sys?.ReleaseOperation();
            }
        }

        public UniTask<LoadResult> DeserializeAsync(
            World world,
            byte[] data,
            RestorationScope restorationScope,
            DeletionPolicy deletionPolicy = DeletionPolicy.ByRegistration,
            CancellationToken cancellationToken = default)
        {
            MainThreadDetector.ThrowIfNotMainThread();
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsCreated) throw new ObjectDisposedException(nameof(world));
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.Cancelled));

            var sys = world.GetExistingSystemManaged<SaveRuntimeSystem>();
            if (sys != null && !sys.TryAcquireOperation())
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.ConcurrentOperation));

            try
            {
                var op = new Operation(OperationKind.Deserialize, cancellationToken);
                if (sys != null) sys.SetCurrent(op);

                var inner = new DeserializeOperation(
                    _definition, world, data, restorationScope, deletionPolicy);

                op.SetPhase(OperationPhase.D1_StructureValidation);
                var result = inner.Execute(cancellationToken);

                op.SetPhase(OperationPhase.P8_Commit);
                op.MarkComplete();

                return UniTask.FromResult(result);
            }
            catch (Format.FormatException fe)
            {
                return UniTask.FromResult(LoadResult.Failure(MapFormatError(fe.Error)));
            }
            catch
            {
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed));
            }
            finally
            {
                sys?.ReleaseOperation();
            }
        }

        // ── 非同期実行（Phase 7d、呼び出しは即座に戻る）──

        public UniTask<SaveResult> SerializeAsyncNonBlocking(
            World world,
            CancellationToken cancellationToken = default)
        {
            MainThreadDetector.ThrowIfNotMainThread();
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsCreated) throw new ObjectDisposedException(nameof(world));

            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.Cancelled));

            var tcs = AutoResetUniTaskCompletionSource<SaveResult>.Create();
            var sys = world.GetExistingSystemManaged<SaveRuntimeSystem>();
            var notifier = new PlayerLoopCompletionNotifier();

            if (sys != null && !sys.TryAcquireOperation())
            {
                notifier.Uninstall();
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.ConcurrentOperation));
            }

            bool executed = false;

            notifier.Install(() =>
            {
                if (executed) return;
                executed = true;

                try
                {
                    var op = new Operation(OperationKind.Serialize, cancellationToken);
                    if (sys != null) sys.SetCurrent(op);

                    op.SetPhase(OperationPhase.S1_AwaitSnapshot);

                    try
                    {
                        var inner = new SerializeOperation(_definition, world);
                        var result = inner.Execute(cancellationToken);

                        op.SetPhase(OperationPhase.S4_Complete);
                        op.MarkComplete();
                        tcs.TrySetResult(result);
                    }
                    catch (Format.FormatException fe)
                    {
                        tcs.TrySetResult(SaveResult.Failure(MapFormatError(fe.Error)));
                    }
                    catch
                    {
                        tcs.TrySetResult(SaveResult.Failure(SaveLoadErrorCode.SerializationFailed));
                    }
                }
                finally
                {
                    notifier.Uninstall();
                    sys?.ReleaseOperation();
                }
            });

            return tcs.Task;
        }

        public UniTask<LoadResult> DeserializeAsyncNonBlocking(
            World world,
            byte[] data,
            RestorationScope restorationScope,
            DeletionPolicy deletionPolicy = DeletionPolicy.ByRegistration,
            CancellationToken cancellationToken = default)
        {
            MainThreadDetector.ThrowIfNotMainThread();
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsCreated) throw new ObjectDisposedException(nameof(world));
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.Cancelled));

            var tcs = AutoResetUniTaskCompletionSource<LoadResult>.Create();
            var sys = world.GetExistingSystemManaged<SaveRuntimeSystem>();
            var notifier = new PlayerLoopCompletionNotifier();

            if (sys != null && !sys.TryAcquireOperation())
            {
                notifier.Uninstall();
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.ConcurrentOperation));
            }

            bool executed = false;

            notifier.Install(() =>
            {
                if (executed) return;
                executed = true;

                try
                {
                    var op = new Operation(OperationKind.Deserialize, cancellationToken);
                    if (sys != null) sys.SetCurrent(op);

                    op.SetPhase(OperationPhase.D1_StructureValidation);

                    try
                    {
                        var inner = new DeserializeOperation(
                            _definition, world, data, restorationScope, deletionPolicy);
                        var result = inner.Execute(cancellationToken);

                        op.SetPhase(OperationPhase.P8_Commit);
                        op.MarkComplete();
                        tcs.TrySetResult(result);
                    }
                    catch (Format.FormatException fe)
                    {
                        tcs.TrySetResult(LoadResult.Failure(MapFormatError(fe.Error)));
                    }
                    catch
                    {
                        tcs.TrySetResult(LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed));
                    }
                }
                finally
                {
                    notifier.Uninstall();
                    sys?.ReleaseOperation();
                }
            });

            return tcs.Task;
        }

        private static SaveLoadErrorCode MapFormatError(Format.FormatError e)
        {
            switch (e)
            {
                case Format.FormatError.CorruptedData: return SaveLoadErrorCode.CorruptedData;
                case Format.FormatError.VersionMismatch: return SaveLoadErrorCode.VersionMismatch;
                case Format.FormatError.ChecksumMismatch: return SaveLoadErrorCode.ChecksumMismatch;
                case Format.FormatError.ValidationFailed: return SaveLoadErrorCode.ValidationFailed;
                case Format.FormatError.EntityReferenceInvalid: return SaveLoadErrorCode.EntityReferenceInvalid;
                case Format.FormatError.SchemaMismatch: return SaveLoadErrorCode.SchemaMismatch;
                default: return SaveLoadErrorCode.CorruptedData;
            }
        }
    }
}
