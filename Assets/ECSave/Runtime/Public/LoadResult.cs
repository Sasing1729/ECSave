// Runtime/Public/LoadResult.cs
using System.Collections.Generic;

namespace ECSave
{
    /// <summary>Deserialize の結果（外部設計 13.2）。</summary>
    public readonly struct LoadResult
    {
        public bool IsSuccess { get; }
        public SaveLoadErrorCode ErrorCode { get; }
        public IReadOnlyList<LoadWarning> Warnings { get; }
        public int EntityCount { get; }

        private LoadResult(bool success, SaveLoadErrorCode code,
            IReadOnlyList<LoadWarning> warnings, int entityCount)
        {
            IsSuccess = success;
            ErrorCode = code;
            Warnings = warnings ?? System.Array.Empty<LoadWarning>();
            EntityCount = entityCount;
        }

        public static LoadResult Success(int entityCount, IReadOnlyList<LoadWarning> warnings = null)
            => new LoadResult(true, SaveLoadErrorCode.None, warnings, entityCount);

        public static LoadResult Failure(SaveLoadErrorCode code, int entityCount = 0,
            IReadOnlyList<LoadWarning> warnings = null)
        {
            if (code == SaveLoadErrorCode.None)
                throw new System.ArgumentException("Failure requires non-None error code", nameof(code));
            return new LoadResult(false, code, warnings, entityCount);
        }
    }
}
