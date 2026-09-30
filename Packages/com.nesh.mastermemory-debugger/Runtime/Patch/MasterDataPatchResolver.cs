using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Resolves patch identity, registered tables and primary keys without depending on the public patch facade.
    /// </summary>
    internal static class MasterDataPatchResolver
    {
        internal static bool IsSameMasterVersion(string a, string b)
        {
            return string.Equals(
                string.IsNullOrEmpty(a) ? MasterMemoryDebugRegistry.UnknownMasterVersion : a,
                string.IsNullOrEmpty(b) ? MasterMemoryDebugRegistry.UnknownMasterVersion : b,
                StringComparison.Ordinal);
        }

        internal static MasterMemoryTableDescriptor FindTable(MasterDataPatchTable patchTable)
        {
            if (patchTable.TableName != null
                && MasterMemoryDebugRegistry.TryGetTable(patchTable.TableName, out var table))
                return table;

            foreach (var candidate in MasterMemoryDebugRegistry.Tables)
            {
                if (patchTable.RecordType != null && candidate.RecordType.FullName == patchTable.RecordType)
                    return candidate;
            }
            return null;
        }

        internal static MasterDataJsonObject CreatePrimaryKeyJson(MasterMemoryTableDescriptor table, object record)
        {
            var json = new MasterDataJsonObject();
            var keyFields = table.TypeDescriptor.PrimaryKeyFields;
            if (keyFields.Count > 0)
            {
                foreach (var field in keyFields)
                    json.Add(field.Name, MasterDataValueUtility.ToJson(field.GetValue(record)));
                return json;
            }

            var key = table.GetPrimaryKey(record);
            if (key is ITuple tuple)
            {
                for (var i = 0; i < tuple.Length; i++)
                    json.Add("Item" + (i + 1), MasterDataValueUtility.ToJson(tuple[i]));
            }
            else
            {
                json.Add("key", MasterDataValueUtility.ToJson(key));
            }
            return json;
        }

        internal static Dictionary<string, object> BuildOriginalLookupByKeyJson(MasterMemoryTableDescriptor table)
        {
            var lookup = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var record in table.GetAllRecords())
            {
                if (record == null) continue;
                lookup[CreatePrimaryKeyJson(table, record).ToCanonicalString()] = record;
            }
            return lookup;
        }

        /// <summary>Re-reads key values with member types so that, for example, 1001.0 and 1001 match.</summary>
        internal static string NormalizePrimaryKeyJson(MasterMemoryTableDescriptor table, MasterDataJsonObject key)
        {
            var keyFields = table.TypeDescriptor.PrimaryKeyFields;
            if (keyFields.Count == 0) return key.ToCanonicalString();

            var normalized = new MasterDataJsonObject();
            foreach (var field in keyFields)
            {
                if (!key.TryGetValue(field.Name, out var json))
                    throw new FormatException($"'{field.Name}' is missing");
                normalized.Add(
                    field.Name,
                    MasterDataValueUtility.ToJson(MasterDataValueUtility.FromJson(json, field.FieldType)));
            }
            return normalized.ToCanonicalString();
        }
    }
}
