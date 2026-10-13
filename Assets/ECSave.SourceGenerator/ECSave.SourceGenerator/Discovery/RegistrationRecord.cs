using System;
using System.Collections.Generic;
using ECSave.SourceGenerator.Core;
using ECSave.SourceGenerator.Emission;
using Microsoft.CodeAnalysis;

namespace ECSave.SourceGenerator.Discovery
{
    internal sealed class RegistrationRecord : IEquatable<RegistrationRecord>
    {
        public RegistrationKind Kind { get; }
        public string BuilderTypeFullName { get; }
        public string TypeArgumentFullName { get; }
        public string FieldPath { get; }
        public int Order { get; }
        public ModuleClass ClassifiedAs { get; }
        public TypePlan Plan { get; }
        public byte[] SchemaHashInput { get; }
        public ulong SchemaHash { get; }
        public FilterInfo Filter { get; }
        public bool WithoutEnableableState { get; }
        public string RenamedFromOldName { get; }
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public string MigrationOldTypeFullName { get; }
        public string MigrationNewTypeFullName { get; }
        public TypePlan MigrationOldPlan { get; }
        public TypePlan MigrationNewPlan { get; }
        public ulong MigrationOldSchemaHash { get; }
        public ulong MigrationNewSchemaHash { get; }
        public ulong MigrationNewTypeNameHash { get; }

        public RegistrationRecord(
            RegistrationKind kind,
            string builderTypeFullName,
            string typeArgumentFullName,
            int order,
            ModuleClass classifiedAs,
            TypePlan plan,
            byte[] schemaHashInput,
            ulong schemaHash,
            IReadOnlyList<Diagnostic> diagnostics = null,
            string fieldPath = null,
            FilterInfo filter = null,
            bool withoutEnableableState = false,
            string renamedFromOldName = null,
            string migrationOldTypeFullName = null,
            string migrationNewTypeFullName = null,
            TypePlan migrationOldPlan = null,
            TypePlan migrationNewPlan = null,
            ulong migrationOldSchemaHash = 0UL,
            ulong migrationNewSchemaHash = 0UL,
            ulong migrationNewTypeNameHash = 0UL)
        {
            Kind = kind;
            BuilderTypeFullName = builderTypeFullName ?? string.Empty;
            TypeArgumentFullName = typeArgumentFullName ?? string.Empty;
            FieldPath = fieldPath ?? string.Empty;
            Order = order;
            ClassifiedAs = classifiedAs;
            Plan = plan;
            SchemaHashInput = schemaHashInput ?? Array.Empty<byte>();
            SchemaHash = schemaHash;
            Filter = filter ?? FilterInfo.Empty;
            WithoutEnableableState = withoutEnableableState;
            RenamedFromOldName = renamedFromOldName;
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
            MigrationOldTypeFullName = migrationOldTypeFullName;
            MigrationNewTypeFullName = migrationNewTypeFullName;
            MigrationOldPlan = migrationOldPlan;
            MigrationNewPlan = migrationNewPlan;
            MigrationOldSchemaHash = migrationOldSchemaHash;
            MigrationNewSchemaHash = migrationNewSchemaHash;
            MigrationNewTypeNameHash = migrationNewTypeNameHash;
        }

        public bool Equals(RegistrationRecord other)
        {
            if (other == null) return false;
            return Kind == other.Kind
                && TypeArgumentFullName == other.TypeArgumentFullName
                && FieldPath == other.FieldPath
                && Order == other.Order
                && ClassifiedAs == other.ClassifiedAs
                && SchemaHash == other.SchemaHash
                && Filter.Equals(other.Filter)
                && WithoutEnableableState == other.WithoutEnableableState
                && RenamedFromOldName == other.RenamedFromOldName
                && MigrationOldTypeFullName == other.MigrationOldTypeFullName
                && MigrationNewTypeFullName == other.MigrationNewTypeFullName
                && MigrationOldSchemaHash == other.MigrationOldSchemaHash
                && MigrationNewSchemaHash == other.MigrationNewSchemaHash
                && MigrationNewTypeNameHash == other.MigrationNewTypeNameHash;
        }

        public override bool Equals(object obj) => Equals(obj as RegistrationRecord);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + (int)Kind;
                h = h * 31 + TypeArgumentFullName.GetHashCode();
                h = h * 31 + FieldPath.GetHashCode();
                h = h * 31 + Order;
                h = h * 31 + (int)ClassifiedAs;
                h = h * 31 + SchemaHash.GetHashCode();
                h = h * 31 + Filter.GetHashCode();
                h = h * 31 + WithoutEnableableState.GetHashCode();
                h = h * 31 + (RenamedFromOldName?.GetHashCode() ?? 0);
                h = h * 31 + (MigrationOldTypeFullName?.GetHashCode() ?? 0);
                h = h * 31 + (MigrationNewTypeFullName?.GetHashCode() ?? 0);
                h = h * 31 + MigrationOldSchemaHash.GetHashCode();
                h = h * 31 + MigrationNewSchemaHash.GetHashCode();
                h = h * 31 + MigrationNewTypeNameHash.GetHashCode();
                return h;
            }
        }
    }
}
