using System;
using System.Collections.Generic;

namespace ECSave.Internal
{
    internal sealed class PrefabMigrationTable
    {
        private readonly Dictionary<ulong, PrefabMigrationStepBase> _byOldHash
            = new Dictionary<ulong, PrefabMigrationStepBase>();

        public int Count => _byOldHash.Count;

        public static PrefabMigrationTable From(IReadOnlyList<PrefabMigrationStepBase> steps)
        {
            var table = new PrefabMigrationTable();
            if (steps == null) return table;
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i];
                if (s == null) continue;
                table.Register(s);
            }
            return table;
        }

        public void Register(PrefabMigrationStepBase step)
        {
            if (step == null) throw new ArgumentNullException(nameof(step));

            if (_byOldHash.ContainsKey(step.OldPrefabSchemaHash))
                throw new InvalidOperationException(
                    $"Prefab migration already registered for Old={step.OldPrefabSchemaHash:X16}");

            _byOldHash.Add(step.OldPrefabSchemaHash, step);
        }

        public PrefabMigrationStepBase Resolve(ulong oldHash)
        {
            _byOldHash.TryGetValue(oldHash, out var step);
            return step;
        }

        public PrefabMigrationStepBase[] ResolveChain(ulong oldHash, ulong targetHash)
        {
            if (oldHash == targetHash) return Array.Empty<PrefabMigrationStepBase>();

            const int MaxChain = 32;
            var chain = new List<PrefabMigrationStepBase>();
            ulong current = oldHash;

            for (int i = 0; i < MaxChain; i++)
            {
                if (!_byOldHash.TryGetValue(current, out var step)) return null;
                chain.Add(step);
                current = step.NewPrefabSchemaHash;
                if (current == targetHash) return chain.ToArray();
            }
            return null;
        }
    }
}
