using System.Collections.Generic;

namespace ECSave.Internal
{
    /// <summary>
    /// 登録済み Migration の表（11.1節）。
    /// キーは (新型名ハッシュ, 旧 SchemaHash)。
    /// MigrationStepBase は class のため NativeParallelHashMap は使えず、
    /// Managed Dictionary を使う。メインスレッドのみで触る。
    /// </summary>
    public sealed class MigrationTable
    {
        private readonly Dictionary<MigrationKey, MigrationStepBase> _byKey
            = new Dictionary<MigrationKey, MigrationStepBase>();

        /// <summary>旧名→現行型名のマップ（RenamedFrom、11.4.1節）。</summary>
        private readonly Dictionary<ulong, ulong> _renamedTo
            = new Dictionary<ulong, ulong>();

        public int Count => _byKey.Count;

        /// <summary>
        /// 登録する。同じキーの重複登録は登録時エラー（11.1節）。
        /// </summary>
        public void Register(MigrationStepBase step)
        {
            if (step == null) throw new System.ArgumentNullException(nameof(step));

            // ▼ 修正：メソッド呼び出し → プロパティ参照
            var key = new MigrationKey(step.NewTypeNameHash, step.OldSchemaHash);

            if (_byKey.ContainsKey(key))
                throw new System.InvalidOperationException(
                    $"Migration already registered for New={key.NewTypeNameHash:X16}, Old={key.OldSchemaHash:X16}");
            _byKey.Add(key, step);
        }

        /// <summary>連鎖の1ステップを解決する。無ければ null。</summary>
        public MigrationStepBase Resolve(ulong newTypeNameHash, ulong oldSchemaHash)
        {
            _byKey.TryGetValue(new MigrationKey(newTypeNameHash, oldSchemaHash), out var step);
            return step;
        }

        /// <summary>RenamedFrom を登録する（11.4.1節）。</summary>
        public void RegisterRenamed(ulong oldFullNameHash, ulong newFullNameHash)
        {
            if (_renamedTo.ContainsKey(oldFullNameHash))
                throw new System.InvalidOperationException(
                    $"RenamedFrom already registered for 0x{oldFullNameHash:X16}");
            _renamedTo.Add(oldFullNameHash, newFullNameHash);
        }

        public bool TryResolveRenamed(ulong oldFullNameHash, out ulong newFullNameHash)
            => _renamedTo.TryGetValue(oldFullNameHash, out newFullNameHash);
    }
}
