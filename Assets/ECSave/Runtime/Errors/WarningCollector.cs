using System.Collections.Generic;
using Unity.Collections;

namespace ECSave.Internal
{
    public struct WarningCollector : System.IDisposable
    {
        private NativeList<WarningRecord> _records;

        public WarningCollector(Allocator allocator)
        {
            _records = new NativeList<WarningRecord>(16, allocator);
        }

        public int Count => _records.IsCreated ? _records.Length : 0;

        public void Add(in WarningRecord record)
        {
            if (!_records.IsCreated)
                throw new System.InvalidOperationException("WarningCollector is disposed");
            _records.Add(record);
        }

        public void ToLoadWarnings(
            List<LoadWarning> output,
            IReadOnlyDictionary<uint, string> typeNameMap)
        {
            if (output == null) return;
            if (!_records.IsCreated) return;

            for (int i = 0; i < _records.Length; i++)
            {
                var r = _records[i];

                string typeName = null;
                if (typeNameMap != null)
                    typeNameMap.TryGetValue(r.TypeSectionId, out typeName);

                output.Add(new LoadWarning(
                    r.KindEnum,
                    r.SourceEntity,
                    typeName,
                    r.SaveEntityIndex,
                    r.ExtraHash));
            }
        }

        public void Dispose()
        {
            if (_records.IsCreated)
            {
                _records.Dispose();
                _records = default;
            }
        }
    }
}
