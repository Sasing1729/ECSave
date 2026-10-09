using Unity.Burst;

namespace ECSave.Tests
{
    /// <summary>
    /// Burst コンパイル済みコードとして実行されているかを判定する。
    /// [BurstDiscard] のメソッドは Burst 下では呼ばれないため、戻り値が false なら Burst 実行。
    /// </summary>
    public static class BurstProbe
    {
        [BurstDiscard]
        static void MarkManaged(ref bool managed) => managed = true;

        public static bool IsManaged()
        {
            bool managed = false;
            MarkManaged(ref managed);
            return managed;
        }
    }
}
