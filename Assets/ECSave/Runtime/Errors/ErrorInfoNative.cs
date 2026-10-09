// Runtime/Errors/ErrorInfoNative.cs
using System.Threading;

namespace ECSave.Internal
{
    /// <summary>
    /// Burst ジョブ内で記録するエラー情報（15.2節）。
    /// 先着記録（first-write-wins）を Interlocked.CompareExchange で行う。
    /// 
    /// レイアウト：
    ///   int _packedCode  … bits 0-15: ErrorCode (u16)
    ///                      bits 16-23: Phase (u8)
    ///                      bits 24-31: reserved
    ///   uint SectionId
    ///   uint RecordIndex
    /// 
    /// パック int を単一のアトミックフィールドとして扱い、
    /// SectionId / RecordIndex は CAS 成功後にのみ書く。
    /// </summary>
    public struct ErrorInfoNative
    {
        private int _packedCode;
        public uint SectionId;
        public uint RecordIndex;

        public ushort ErrorCodeRaw => (ushort)(_packedCode & 0xFFFF);
        public SaveLoadErrorCode Code => (SaveLoadErrorCode)ErrorCodeRaw;
        public byte Phase => (byte)((_packedCode >> 16) & 0xFF);
        public bool IsNone => (_packedCode & 0xFFFF) == 0;

        public static ErrorInfoNative None => default;

        public static ErrorInfoNative Create(
            SaveLoadErrorCode code, byte phase,
            uint sectionId = 0, uint recordIndex = 0)
        {
            int packed = ((int)code & 0xFFFF) | ((phase & 0xFF) << 16);
            return new ErrorInfoNative
            {
                _packedCode = packed,
                SectionId = sectionId,
                RecordIndex = recordIndex,
            };
        }

        /// <summary>
        /// 先着記録（15.2節）。既にエラーが記録されていれば false。
        /// </summary>
        public static bool TryRecord(ref ErrorInfoNative target, in ErrorInfoNative info)
        {
            if (info.IsNone) return false;

            int newPacked = info._packedCode;
            int observed = Interlocked.CompareExchange(ref target._packedCode, newPacked, 0);
            if (observed != 0)
                return false; // 既に誰かが記録済み

            // CAS 成功者は1人だけなので、以降の書き込みは競合しない
            target.SectionId = info.SectionId;
            target.RecordIndex = info.RecordIndex;
            return true;
        }

        /// <summary>記録済みかどうか。</summary>
        public bool IsRecorded => Volatile.Read(ref _packedCode) != 0;

        /// <summary>
        /// 記録を消す。テスト用。プロダクションでは使わない。
        /// </summary>
        internal void ResetForTests()
        {
            Interlocked.Exchange(ref _packedCode, 0);
            SectionId = 0;
            RecordIndex = 0;
        }
    }
}
