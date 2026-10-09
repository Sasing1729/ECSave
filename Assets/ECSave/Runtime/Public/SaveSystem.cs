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

        public UniTask<SaveResult> SerializeAsync(
            World world,
            CancellationToken cancellationToken = default)
        {
            MainThreadDetector.ThrowIfNotMainThread();

            if (world == null) throw new ArgumentNullException(nameof(world));
            if (!world.IsCreated) throw new ObjectDisposedException(nameof(world));

            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.Cancelled));

            try
            {
                var op = new SerializeOperation(_definition, world);
                var result = op.Execute(cancellationToken);
                return UniTask.FromResult(result);
            }
            catch (Format.FormatException fe)
            {
                return UniTask.FromResult(SaveResult.Failure(MapFormatError(fe.Error)));
            }
            catch (Exception)
            {
                return UniTask.FromResult(SaveResult.Failure(SaveLoadErrorCode.SerializationFailed));
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

            try
            {
                var op = new DeserializeOperation(
                    _definition, world, data, restorationScope, deletionPolicy);
                var result = op.Execute(cancellationToken);
                return UniTask.FromResult(result);
            }
            catch (Format.FormatException fe)
            {
                return UniTask.FromResult(LoadResult.Failure(MapFormatError(fe.Error)));
            }
            catch (Exception)
            {
                return UniTask.FromResult(LoadResult.Failure(SaveLoadErrorCode.WorldModificationFailed));
            }
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
