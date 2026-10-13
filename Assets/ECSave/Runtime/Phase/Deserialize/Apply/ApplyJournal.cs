using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace ECSave.Internal
{
    internal sealed class ApplyJournal : IDisposable
    {
        private readonly List<ModuleBackup> _backups;
        private readonly NativeList<Entity> _createdEntities;
        private readonly Allocator _allocator;
        private bool _disposed;

        public ApplyJournal(Allocator allocator)
        {
            _allocator = allocator;
            _backups = new List<ModuleBackup>(8);
            _createdEntities = new NativeList<Entity>(64, allocator);
        }

        public void Backup(
            SaveDefinition def,
            NativeArray<Entity> updateTargets,
            EntityManager em)
        {
            for (int i = 0; i < def.Modules.Count; i++)
            {
                var m = def.Modules[i];
                var b = m.BackupForRollback(updateTargets, em, _allocator);
                _backups.Add(b);
            }
        }

        public void NoteCreated(Entity entity)
        {
            if (entity != Entity.Null)
                _createdEntities.Add(entity);
        }

        public bool Rollback(NativeArray<Entity> updateTargets, EntityManager em)
        {
            try
            {
                for (int i = _createdEntities.Length - 1; i >= 0; i--)
                {
                    var e = _createdEntities[i];
                    if (e != Entity.Null && em.Exists(e))
                        em.DestroyEntity(e);
                }

                for (int i = 0; i < _backups.Count; i++)
                {
                    var b = _backups[i];
                    b.Module.RestoreFromRollback(updateTargets, in b, em);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            for (int i = 0; i < _backups.Count; i++)
            {
                var b = _backups[i];
                if (b.Values.IsCreated) b.Values.Dispose();
                if (b.Presence.IsCreated) b.Presence.Dispose();
                if (b.Enabled.IsCreated) b.Enabled.Dispose();
                if (b.BufferCounts.IsCreated) b.BufferCounts.Dispose();
                if (b.AnchorEntities.IsCreated) b.AnchorEntities.Dispose();
                if (b.AnchorValues.IsCreated) b.AnchorValues.Dispose();
            }
            _backups.Clear();

            if (_createdEntities.IsCreated) _createdEntities.Dispose();
        }
    }
}
