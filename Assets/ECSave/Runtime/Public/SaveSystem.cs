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

        private static void LogIfDevelopment(Exception ex, string context)
        {
#if DEBUG
            if (ex == null) return;
            UnityEngine.Debug.LogError($"[ECSave] {context}: {ex}");
#endif
        }

        private static bool IsQueryValidForWorld(World world, EntityQuery query)
        {
            try
            {
                return world.EntityManager.IsQueryValid(query);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (Exception ex)
            {
                LogIfDevelopment(ex, "IsQueryValid threw unexpectedly");
                return false;
            }
        }

        private static void ValidateRestorationScope(World world, in RestorationScope scope)
        {
            if (scope.IsEntireWorld) return;

            if (!IsQueryValidForWorld(world, scope.Query))
            {
                throw new ArgumentException(
                    "RestorationScope.Custom requires an EntityQuery that belongs to the target World " +
                    "and has not been disposed. Pass RestorationScope.EntireWorld, or create the query " +
                    "with the same World's EntityManager (em.CreateEntityQuery(...)). " +
                    "Passing a query from another World or a disposed query would result in undefined behavior " +
                    "when the deletion phase attempts to destroy entities outside the target World.",
                    "restorationScope");
            }
        }

        /// <summary>
        /// Serialize を実行する。
        ///
        /// <para>
        /// <b>実行モデル</b>:
        /// <see cref="StreamingConsistencyMode.TimeBased"/>（既定）では
        /// <b>メインスレッド上で同期的に実行</b>し、完了済みの
        /// <see cref="Cysharp.Threading.Tasks.UniTask{T}"/> を返す。
        /// 真の非同期（フレーム跨ぎ）は <see cref="SerializeAsyncNonBlocking"/>
        /// または <see cref="StreamingConsistencyMode.FrameBoundary"/> を使用すること（ADR-21）。
        /// </para>
        ///
        /// <para>
        /// <b>ブロッキングの注意</b>: <see cref="StreamingConsistencyMode.FrameBoundary"/>
        /// の場合、本メソッドが返す <see cref="UniTask{T}"/> に対して
        /// <c>.GetAwaiter().GetResult()</c> を呼ぶと <b>メインスレッドがデッドロック</b>
        /// する（PlayerLoop の完了通知を待つため）。呼び出し側は <c>await</c> で
        /// 待機すること。
        /// </para>
        ///
        /// <para>
        /// <b>命名</b>: 旧名 <c>SerializeAsync</c> から改名。実装は
        /// <see cref="StreamingConsistencyMode.TimeBased"/> では同期的に実行されるため、
        /// 名前に <c>Async</c> を含めない。
        /// </para>
        /// </summary>
        public UniTask<SaveResult> Serialize(
            World world,
            CancellationToken cancellationToken = default)
        {
            MainThreadDetector.ThrowIfNotMainThread();
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsCreated) throw new ObjectDisposedException(nameof(world));

            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.Cancelled));

            var mode = _definition.SubScene != null
                ? _definition.SubScene.StreamingConsistency
                : StreamingConsistencyMode.TimeBased;

            if (mode == StreamingConsistencyMode.FrameBoundary)
                return SerializeFrameBoundaryAsync(world, cancellationToken);

            var sys = world.GetExistingSystemManaged<SaveRuntimeSystem>();
            if (sys != null && !sys.TryAcquireOperation())
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.ConcurrentOperation));

            try
            {
                var op = new Operation(OperationKind.Serialize, cancellationToken);
                if (sys != null) sys.SetCurrent(op);

                var inner = new SerializeOperation(_definition, world, p => op.SetPhase(p));

                op.SetPhase(OperationPhase.S1_AwaitSnapshot);
                var result = inner.Execute(cancellationToken);

                op.SetPhase(OperationPhase.S4_Complete);
                op.MarkComplete();

                return UniTask.FromResult(result);
            }
            catch (Format.FormatException fe)
            {
                return UniTask.FromResult(SaveResult.Failure(ErrorInfoMapper.Map(fe.Error)));
            }
            catch (Exception ex)
            {
                LogIfDevelopment(ex, "Serialize failed");
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.SerializationFailed));
            }
            finally
            {
                sys?.SetCurrent(null);
                sys?.ReleaseOperation();
            }
        }

        private UniTask<SaveResult> SerializeFrameBoundaryAsync(World world, CancellationToken ct)
        {
            var sys = world.GetExistingSystemManaged<SaveRuntimeSystem>();
            if (sys == null)
                return SerializeSyncFallback(world, ct);

            if (!sys.TryAcquireOperation())
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.ConcurrentOperation));

            var tcs = AutoResetUniTaskCompletionSource<SaveResult>.Create();
            var op = new Operation(OperationKind.Serialize, ct);
            var inner = new SerializeOperation(_definition, world, p => op.SetPhase(p));

            op.SetDrive(o =>
            {
                bool done;
                try
                {
                    done = inner.MoveNext(o.CancellationToken);
                }
                catch (Exception ex)
                {
                    LogIfDevelopment(ex, "FrameBoundary SerializeOperation threw");
                    tcs.TrySetResult(SaveResult.Failure(SaveLoadErrorCode.SerializationFailed));
                    return true;
                }

                if (done)
                {
                    o.SetPhase(OperationPhase.S4_Complete);
                    tcs.TrySetResult(inner.Result);
                }

                return done;
            });

            op.OnAbort = code =>
            {
                try { inner.Dispose(); }
                catch (Exception ex) { LogIfDevelopment(ex, "SerializeOperation.Dispose failed during abort"); }
                tcs.TrySetResult(SaveResult.Failure(code));
            };

            op.OnComplete = _ =>
            {
                try { inner.Dispose(); }
                catch (Exception ex) { LogIfDevelopment(ex, "SerializeOperation.Dispose failed during complete"); }
                sys.SetCurrent(null);
                sys.ReleaseOperation();
            };

            sys.SetCurrent(op);
            return tcs.Task;
        }

        private UniTask<SaveResult> SerializeSyncFallback(World world, CancellationToken ct)
        {
            try
            {
                var inner = new SerializeOperation(_definition, world);
                return UniTask.FromResult(inner.Execute(ct));
            }
            catch (Format.FormatException fe)
            {
                return UniTask.FromResult(SaveResult.Failure(ErrorInfoMapper.Map(fe.Error)));
            }
            catch (Exception ex)
            {
                LogIfDevelopment(ex, "SerializeSyncFallback failed");
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.SerializationFailed));
            }
        }

        /// <summary>
        /// Deserialize を実行する。
        ///
        /// <para>
        /// <b>実行モデル</b>:
        /// <see cref="StreamingConsistencyMode.TimeBased"/>（既定）では
        /// <b>メインスレッド上で同期的に実行</b>し、完了済みの
        /// <see cref="Cysharp.Threading.Tasks.UniTask{T}"/> を返す。
        /// 真の非同期（フレーム跨ぎ）は <see cref="DeserializeAsyncNonBlocking"/>
        /// または <see cref="StreamingConsistencyMode.FrameBoundary"/> を使用すること（ADR-21）。
        /// </para>
        ///
        /// <para>
        /// <b>ブロッキングの注意</b>: <see cref="StreamingConsistencyMode.FrameBoundary"/>
        /// の場合、<c>.GetAwaiter().GetResult()</c> はメインスレッドで
        /// デッドロックする。呼び出し側は <c>await</c> で待機すること。
        /// </para>
        ///
        /// <para>
        /// <b>命名</b>: 旧名 <c>DeserializeAsync</c> から改名。実装は
        /// <see cref="StreamingConsistencyMode.TimeBased"/> では同期的に実行されるため、
        /// 名前に <c>Async</c> を含めない。
        /// </para>
        /// </summary>
        public UniTask<LoadResult> Deserialize(
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

            ValidateRestorationScope(world, restorationScope);

            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.Cancelled));

            var mode = _definition.SubScene != null
                ? _definition.SubScene.StreamingConsistency
                : StreamingConsistencyMode.TimeBased;

            if (mode == StreamingConsistencyMode.FrameBoundary)
                return DeserializeFrameBoundaryAsync(world, data, restorationScope, deletionPolicy, cancellationToken);

            var sys = world.GetExistingSystemManaged<SaveRuntimeSystem>();
            if (sys != null && !sys.TryAcquireOperation())
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.ConcurrentOperation));

            try
            {
                var op = new Operation(OperationKind.Deserialize, cancellationToken);
                if (sys != null) sys.SetCurrent(op);

                var inner = new DeserializeOperation(
                    _definition, world, data, restorationScope, deletionPolicy,
                    p => op.SetPhase(p));

                op.SetPhase(OperationPhase.D1_StructureValidation);
                var result = inner.Execute(cancellationToken);

                op.SetPhase(OperationPhase.P8_Commit);
                op.MarkComplete();

                return UniTask.FromResult(result);
            }
            catch (Format.FormatException fe)
            {
                return UniTask.FromResult(LoadResult.Failure(ErrorInfoMapper.Map(fe.Error)));
            }
            catch (Exception ex)
            {
                LogIfDevelopment(ex, "Deserialize failed");
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed));
            }
            finally
            {
                sys?.SetCurrent(null);
                sys?.ReleaseOperation();
            }
        }

        private UniTask<LoadResult> DeserializeFrameBoundaryAsync(
            World world,
            byte[] data,
            RestorationScope scope,
            DeletionPolicy policy,
            CancellationToken ct)
        {
            var sys = world.GetExistingSystemManaged<SaveRuntimeSystem>();
            if (sys == null)
                return DeserializeSyncFallback(world, data, scope, policy, ct);

            if (!sys.TryAcquireOperation())
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.ConcurrentOperation));

            var tcs = AutoResetUniTaskCompletionSource<LoadResult>.Create();
            var op = new Operation(OperationKind.Deserialize, ct);
            var inner = new DeserializeOperation(
                _definition, world, data, scope, policy, p => op.SetPhase(p));

            op.SetDrive(o =>
            {
                bool done;
                try
                {
                    done = inner.MoveNext(o.CancellationToken);
                }
                catch (Exception ex)
                {
                    LogIfDevelopment(ex, "FrameBoundary DeserializeOperation threw");
                    tcs.TrySetResult(LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed));
                    return true;
                }

                if (done)
                {
                    o.SetPhase(OperationPhase.P8_Commit);
                    tcs.TrySetResult(inner.Result);
                }
                else
                {
                    o.SetPhase(inner.CurrentPhase);
                }

                return done;
            });

            op.OnAbort = code =>
            {
                try { inner.Dispose(); }
                catch (Exception ex) { LogIfDevelopment(ex, "DeserializeOperation.Dispose failed during abort"); }
                tcs.TrySetResult(LoadResult.Failure(code));
            };

            op.OnComplete = _ =>
            {
                try { inner.Dispose(); }
                catch (Exception ex) { LogIfDevelopment(ex, "DeserializeOperation.Dispose failed during complete"); }
                sys.SetCurrent(null);
                sys.ReleaseOperation();
            };

            sys.SetCurrent(op);
            return tcs.Task;
        }

        private UniTask<LoadResult> DeserializeSyncFallback(
            World world,
            byte[] data,
            RestorationScope scope,
            DeletionPolicy policy,
            CancellationToken ct)
        {
            try
            {
                var inner = new DeserializeOperation(_definition, world, data, scope, policy);
                return UniTask.FromResult(inner.Execute(ct));
            }
            catch (Format.FormatException fe)
            {
                return UniTask.FromResult(LoadResult.Failure(ErrorInfoMapper.Map(fe.Error)));
            }
            catch (Exception ex)
            {
                LogIfDevelopment(ex, "DeserializeSyncFallback failed");
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed));
            }
        }

        /// <summary>
        /// NonBlocking 版 Serialize。
        ///
        /// <para>
        /// <b>命名について</b>: 名前は <c>NonBlocking</c> だが、実装は
        /// <b>次の <c>PostLateUpdate</c> で全体を同期実行</b>する
        /// （真の分散非同期ではない）。呼び出し元スレッドをブロックしない
        /// という意味での "NonBlocking" であり、処理自体は 1 フレーム遅延の
        /// 同期実行（ADR-21）。
        /// </para>
        ///
        /// <para>
        /// <b>ブロッキングの注意</b>: 返り値の <c>.GetAwaiter().GetResult()</c>
        /// はメインスレッドでデッドロックする。<c>await</c> で待機すること。
        /// </para>
        /// </summary>
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

            var op = new Operation(OperationKind.Serialize, cancellationToken);
            bool executed = false;

            op.OnAbort = code =>
            {
                try { notifier.Uninstall(); } catch { }
                tcs.TrySetResult(SaveResult.Failure(code));
            };

            if (sys != null) sys.SetCurrent(op);

            notifier.Install(() =>
            {
                if (executed) return;
                executed = true;

                if (op.IsComplete) return;

                try
                {
                    op.SetPhase(OperationPhase.S1_AwaitSnapshot);

                    try
                    {
                        var inner = new SerializeOperation(_definition, world, p => op.SetPhase(p));
                        var result = inner.Execute(cancellationToken);

                        op.SetPhase(OperationPhase.S4_Complete);
                        op.MarkComplete();
                        tcs.TrySetResult(result);
                    }
                    catch (Format.FormatException fe)
                    {
                        tcs.TrySetResult(SaveResult.Failure(ErrorInfoMapper.Map(fe.Error)));
                    }
                    catch (Exception ex)
                    {
                        LogIfDevelopment(ex, "SerializeAsyncNonBlocking failed");
                        tcs.TrySetResult(SaveResult.Failure(SaveLoadErrorCode.SerializationFailed));
                    }
                }
                finally
                {
                    notifier.Uninstall();
                    sys?.SetCurrent(null);
                    sys?.ReleaseOperation();
                }
            });

            return tcs.Task;
        }

        /// <summary>
        /// NonBlocking 版 Deserialize。<see cref="SerializeAsyncNonBlocking"/>
        /// と同じく、次の <c>PostLateUpdate</c> で同期実行し、呼び出し元
        /// スレッドをブロックしない。<c>.GetAwaiter().GetResult()</c> は
        /// メインスレッドでデッドロックする。
        /// </summary>
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

            ValidateRestorationScope(world, restorationScope);

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

            var op = new Operation(OperationKind.Deserialize, cancellationToken);
            bool executed = false;

            op.OnAbort = code =>
            {
                try { notifier.Uninstall(); } catch { }
                tcs.TrySetResult(LoadResult.Failure(code));
            };

            if (sys != null) sys.SetCurrent(op);

            notifier.Install(() =>
            {
                if (executed) return;
                executed = true;

                if (op.IsComplete) return;

                try
                {
                    op.SetPhase(OperationPhase.D1_StructureValidation);

                    try
                    {
                        var inner = new DeserializeOperation(
                            _definition, world, data, restorationScope, deletionPolicy,
                            p => op.SetPhase(p));
                        var result = inner.Execute(cancellationToken);

                        op.SetPhase(OperationPhase.P8_Commit);
                        op.MarkComplete();
                        tcs.TrySetResult(result);
                    }
                    catch (Format.FormatException fe)
                    {
                        tcs.TrySetResult(LoadResult.Failure(ErrorInfoMapper.Map(fe.Error)));
                    }
                    catch (Exception ex)
                    {
                        LogIfDevelopment(ex, "DeserializeAsyncNonBlocking failed");
                        tcs.TrySetResult(LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed));
                    }
                }
                finally
                {
                    notifier.Uninstall();
                    sys?.SetCurrent(null);
                    sys?.ReleaseOperation();
                }
            });

            return tcs.Task;
        }
    }
}
