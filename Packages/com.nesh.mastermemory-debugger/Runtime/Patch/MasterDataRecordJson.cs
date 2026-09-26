using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Full record as JSON (every public member, collections and nested objects included), for "Copy JSON".
    /// Read-only output: it is not a patch format.
    /// </summary>
    public static class MasterDataRecordJson
    {
        const int MaxDepth = 8;

        public static string ToJson(object record, bool pretty = true)
        {
            return MasterDataJson.Serialize(ToJsonValue(record, 0, new HashSet<object>(ReferenceComparer.Instance)), pretty);
        }

        static object ToJsonValue(object value, int depth, HashSet<object> visiting)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (MasterDataValueUtility.GetKind(type) != MasterDataValueKind.Complex) return MasterDataValueUtility.ToJson(value);
            if (value is decimal || value is char || value is DateTime || value is DateTimeOffset || value is TimeSpan || value is Guid)
            {
                return MasterDataValueUtility.Format(value);
            }
            if (depth >= MaxDepth) return MasterDataValueUtility.Format(value);

            var isReference = !type.IsValueType;
            if (isReference && !visiting.Add(value)) return "<cycle>";
            try
            {
                switch (value)
                {
                    case IDictionary dictionary:
                    {
                        var obj = new MasterDataJsonObject();
                        foreach (DictionaryEntry entry in dictionary)
                        {
                            obj[MasterDataValueUtility.Format(entry.Key)] = ToJsonValue(entry.Value, depth + 1, visiting);
                        }
                        return obj;
                    }
                    case IEnumerable enumerable:
                    {
                        var list = new List<object>();
                        foreach (var item in enumerable) list.Add(ToJsonValue(item, depth + 1, visiting));
                        return list;
                    }
                    default:
                    {
                        var obj = new MasterDataJsonObject();
                        foreach (var field in MasterDataReflectionCache.Get(type).Fields)
                        {
                            object member;
                            try
                            {
                                member = field.GetValue(value);
                            }
                            catch (Exception e)
                            {
                                member = "<error: " + e.Message + ">";
                            }
                            obj[field.Name] = ToJsonValue(member, depth + 1, visiting);
                        }
                        return obj;
                    }
                }
            }
            finally
            {
                if (isReference) visiting.Remove(value);
            }
        }

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
