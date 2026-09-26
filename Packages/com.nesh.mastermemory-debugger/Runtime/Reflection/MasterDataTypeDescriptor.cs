using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Cached reflection information of a record type.</summary>
    public sealed class MasterDataTypeDescriptor
    {
        readonly Dictionary<string, MasterMemoryFieldDescriptor> byName;

        internal MasterDataTypeDescriptor(Type type, List<MasterMemoryFieldDescriptor> fields)
        {
            Type = type;
            Fields = fields;
            byName = new Dictionary<string, MasterMemoryFieldDescriptor>(fields.Count, StringComparer.Ordinal);

            var primaryKeys = new List<MasterMemoryFieldDescriptor>();
            var secondaryKeys = new List<MasterMemoryFieldDescriptor>();
            var strings = new List<MasterMemoryFieldDescriptor>();
            foreach (var field in fields)
            {
                byName[field.Name] = field;
                if (field.IsPrimaryKey) primaryKeys.Add(field);
                if (field.IsSecondaryKey) secondaryKeys.Add(field);
                if (field.Kind == MasterDataValueKind.String) strings.Add(field);
            }
            primaryKeys.Sort((a, b) => a.PrimaryKeyOrder.CompareTo(b.PrimaryKeyOrder));

            PrimaryKeyFields = primaryKeys;
            SecondaryKeyFields = secondaryKeys;
            StringFields = strings;
        }

        public Type Type { get; }

        /// <summary>All public members. Primary keys first, then declaration order.</summary>
        public IReadOnlyList<MasterMemoryFieldDescriptor> Fields { get; }

        /// <summary>Members marked with <c>[PrimaryKey]</c>, sorted by KeyOrder.</summary>
        public IReadOnlyList<MasterMemoryFieldDescriptor> PrimaryKeyFields { get; }

        /// <summary>Members marked with <c>[SecondaryKey]</c>.</summary>
        public IReadOnlyList<MasterMemoryFieldDescriptor> SecondaryKeyFields { get; }

        public IReadOnlyList<MasterMemoryFieldDescriptor> StringFields { get; }

        public bool TryGetField(string name, out MasterMemoryFieldDescriptor field)
        {
            return byName.TryGetValue(name, out field);
        }
    }
}
