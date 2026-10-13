using System;
using ECSave.Internal;
using Unity.Entities;

namespace ECSave
{
    public sealed class SaveDefinitionBuilder<T>
    {
        private readonly SaveDefinitionBuilderImpl _impl;

        internal SaveDefinitionBuilder(SaveDefinitionBuilderImpl impl)
        {
            _impl = impl;
        }

        public SaveDefinitionBuilder<T> WithoutEnableableState()
        {
            _impl.RecordWithoutEnableable(typeof(T));
            return this;
        }

        public SaveDefinitionBuilder<T> WithAll<TFilter>() where TFilter : unmanaged, IComponentData
        {
            _impl.RecordWithAll(typeof(T), typeof(TFilter));
            return this;
        }

        public SaveDefinitionBuilder<T> WithAny<TFilter>() where TFilter : unmanaged, IComponentData
        {
            _impl.RecordWithAny(typeof(T), typeof(TFilter));
            return this;
        }

        public SaveDefinitionBuilder<T> WithNone<TFilter>() where TFilter : unmanaged, IComponentData
        {
            _impl.RecordWithNone(typeof(T), typeof(TFilter));
            return this;
        }

        public SaveDefinitionBuilder<T> RenamedFrom(string oldFullName)
        {
            if (string.IsNullOrEmpty(oldFullName))
                throw new SaveRegistrationException("ECSAVE-R005", "RenamedFrom requires a non-empty old name");
            _impl.RecordRenamedFrom(typeof(T), oldFullName);
            return this;
        }
    }
}
