using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Identifies an overridden record: record type + primary key (composite keys are ValueTuples).</summary>
    public readonly struct MasterDataOverrideKey : IEquatable<MasterDataOverrideKey>
    {
        public readonly Type RecordType;
        public readonly object PrimaryKey;

        public MasterDataOverrideKey(Type recordType, object primaryKey)
        {
            RecordType = recordType ?? throw new ArgumentNullException(nameof(recordType));
            PrimaryKey = primaryKey ?? throw new ArgumentNullException(nameof(primaryKey));
        }

        public bool Equals(MasterDataOverrideKey other)
        {
            return RecordType == other.RecordType && EqualityComparer<object>.Default.Equals(PrimaryKey, other.PrimaryKey);
        }

        public override bool Equals(object obj) => obj is MasterDataOverrideKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((RecordType?.GetHashCode() ?? 0) * 397) ^ (PrimaryKey?.GetHashCode() ?? 0);
            }
        }

        public override string ToString() => $"{RecordType?.Name}:{MasterDataValueUtility.FormatKey(PrimaryKey)}";
    }
}
