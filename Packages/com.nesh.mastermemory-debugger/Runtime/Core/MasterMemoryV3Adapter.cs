using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MasterMemory.Meta;
using MasterMemory.Validation;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Boundary around MasterMemory generated-code conventions. Code outside this adapter should not know method names
    /// such as GetRawDataUnsafe, PrimaryKeySelector, ToImmutableBuilder, Diff or Remove{RecordType}.
    /// </summary>
    internal interface IMasterMemoryAdapter
    {
        MasterMemoryTableDescriptor CreateTable(
            MetaTable metaTable,
            Func<string, object> getTable,
            Func<Type, Func<object, string>> displayNameFactory);

        MasterMemoryBuilderInfo GetBuilderInfo(Type databaseType);
    }

    internal sealed class MasterMemoryBuilderInfo
    {
        public MethodInfo ToImmutableBuilder;
        public MethodInfo Build;
        public MethodInfo Validate;
        public readonly Dictionary<Type, MethodInfo> Diff = new Dictionary<Type, MethodInfo>();
        public readonly Dictionary<Type, MethodInfo> Remove = new Dictionary<Type, MethodInfo>();
    }

    /// <summary>Adapter for the MasterMemory v3 generated API used by this package.</summary>
    internal sealed class MasterMemoryV3Adapter : IMasterMemoryAdapter
    {
        readonly Dictionary<Type, MasterMemoryBuilderInfo> builders = new Dictionary<Type, MasterMemoryBuilderInfo>();

        public MasterMemoryTableDescriptor CreateTable(
            MetaTable metaTable,
            Func<string, object> getTable,
            Func<Type, Func<object, string>> displayNameFactory)
        {
            if (metaTable == null) throw new ArgumentNullException(nameof(metaTable));
            if (getTable == null) throw new ArgumentNullException(nameof(getTable));

            var memoryTableName = metaTable.TableName;
            var recordType = metaTable.DataType;
            var tableType = metaTable.TableType;

            var rawDataMethod = tableType.GetMethod(
                "GetRawDataUnsafe", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(tableType.FullName, "GetRawDataUnsafe");
            var selectorProperty = tableType.GetProperty("PrimaryKeySelector", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(tableType.FullName, "PrimaryKeySelector");
            var keyType = selectorProperty.PropertyType.GetGenericArguments()[1];

            Func<IEnumerable<object>> getAllRecords = () =>
            {
                var table = getTable(memoryTableName);
                if (table == null) return Array.Empty<object>();
                var source = (IEnumerable)rawDataMethod.Invoke(table, null);
                return source as IEnumerable<object> ?? source.Cast<object>();
            };

            Func<object, object> getPrimaryKey;
            var primaryIndex = metaTable.Indexes.FirstOrDefault(x => x.IsPrimaryIndex);
            if (primaryIndex != null && primaryIndex.IndexProperties.Count == 1)
            {
                var keyProperty = primaryIndex.IndexProperties[0];
                getPrimaryKey = record => keyProperty.GetValue(record);
            }
            else
            {
                Delegate selector = null;
                getPrimaryKey = record =>
                {
                    if (selector == null)
                    {
                        var table = getTable(memoryTableName)
                            ?? throw new InvalidOperationException($"Table '{memoryTableName}' is not loaded.");
                        selector = (Delegate)selectorProperty.GetValue(table);
                    }
                    return selector.DynamicInvoke(record);
                };
            }

            return new MasterMemoryTableDescriptor(
                recordType.Name,
                memoryTableName,
                recordType,
                keyType,
                getAllRecords,
                getPrimaryKey,
                displayNameFactory?.Invoke(recordType));
        }

        public MasterMemoryBuilderInfo GetBuilderInfo(Type databaseType)
        {
            if (databaseType == null) throw new ArgumentNullException(nameof(databaseType));
            if (builders.TryGetValue(databaseType, out var cached)) return cached;

            var info = new MasterMemoryBuilderInfo
            {
                ToImmutableBuilder = databaseType.GetMethod(
                    "ToImmutableBuilder", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
                Validate = databaseType.GetMethod(
                    "Validate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
            };
            if (info.Validate != null && info.Validate.ReturnType != typeof(ValidateResult)) info.Validate = null;

            var builderType = info.ToImmutableBuilder?.ReturnType;
            if (builderType != null)
            {
                info.Build = builderType.GetMethod("Build", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                foreach (var method in builderType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name != "Diff") continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType.IsArray)
                    {
                        info.Diff[parameters[0].ParameterType.GetElementType()] = method;
                    }
                }

                foreach (var recordType in info.Diff.Keys)
                {
                    var remove = builderType.GetMethod(
                        "Remove" + recordType.Name, BindingFlags.Public | BindingFlags.Instance);
                    var parameters = remove?.GetParameters();
                    if (parameters != null && parameters.Length == 1 && parameters[0].ParameterType.IsArray)
                        info.Remove[recordType] = remove;
                }
            }

            builders.Add(databaseType, info);
            return info;
        }
    }
}
