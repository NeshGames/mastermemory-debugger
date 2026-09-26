using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MasterMemory.Meta;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Tables are registered by the game project; the package never references concrete master types.
    /// All registration calls are no-ops outside the Editor / Development Builds.
    /// </summary>
    public static class MasterMemoryDebugRegistry
    {
        public const string UnknownMasterVersion = "unknown";

        static readonly List<MasterMemoryTableDescriptor> s_tables = new List<MasterMemoryTableDescriptor>();
        static readonly Dictionary<Type, Func<object, string>> s_displayNameOverrides = new Dictionary<Type, Func<object, string>>();
        static Func<string> s_masterVersionProvider;

        /// <summary>Raised after a table was registered or unregistered.</summary>
        public static event Action TablesChanged;

        public static IReadOnlyList<MasterMemoryTableDescriptor> Tables => s_tables;

        // ------------------------------------------------------------------ registration

        /// <summary>
        /// Registers a table manually.
        /// <code>
        /// MasterMemoryDebugRegistry.RegisterTable&lt;SkillMaster, int&gt;(
        ///     "SkillMaster", () =&gt; db.SkillMasterTable.All, x =&gt; x.Id, x =&gt; x.Name);
        /// </code>
        /// <paramref name="getAllRecords"/> must return the ORIGINAL records (the database the debugger compares against).
        /// </summary>
        public static void RegisterTable<TRecord, TKey>(
            string tableName,
            Func<IEnumerable<TRecord>> getAllRecords,
            Func<TRecord, TKey> getPrimaryKey,
            Func<TRecord, string> getDisplayName = null)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            if (getAllRecords == null) throw new ArgumentNullException(nameof(getAllRecords));
            if (getPrimaryKey == null) throw new ArgumentNullException(nameof(getPrimaryKey));

            var descriptor = new MasterMemoryTableDescriptor(
                tableName,
                typeof(TRecord).GetCustomAttribute<MasterMemory.MemoryTableAttribute>()?.TableName,
                typeof(TRecord),
                typeof(TKey),
                () => ToObjects(getAllRecords()),
                record => getPrimaryKey((TRecord)record),
                getDisplayName != null
                    ? record => getDisplayName((TRecord)record)
                    : CreateDefaultDisplayName(typeof(TRecord)));
            Add(descriptor);
        }

        /// <summary>
        /// Registers every table of a generated MemoryDatabase.
        /// <code>
        /// MasterMemoryDebugRegistry.RegisterDatabase(
        ///     MemoryDatabase.GetMetaDatabase(),
        ///     tableName =&gt; MemoryDatabase.GetTable(originalDatabase, tableName));
        /// </code>
        /// <paramref name="getTable"/> is evaluated every time records are read, so it may return a newly loaded database.
        /// It must return tables of the ORIGINAL database, not a database rebuilt with overrides.
        /// </summary>
        public static void RegisterDatabase(MetaDatabase metaDatabase, Func<string, object> getTable)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            if (metaDatabase == null) throw new ArgumentNullException(nameof(metaDatabase));
            if (getTable == null) throw new ArgumentNullException(nameof(getTable));

            foreach (var metaTable in metaDatabase.GetTableInfos())
            {
                try
                {
                    Add(CreateFromMetaTable(metaTable, getTable));
                }
                catch (Exception e)
                {
                    MasterMemoryDebugLog.Error($"Failed to register table '{metaTable.TableName}': {e}");
                }
            }
        }

        public static bool UnregisterTable(string tableName)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return false;
            var index = s_tables.FindIndex(x => x.TableName == tableName);
            if (index < 0) return false;
            s_tables.RemoveAt(index);
            TablesChanged?.Invoke();
            return true;
        }

        /// <summary>Removes every registered table (overrides are kept).</summary>
        public static void ClearTables()
        {
            if (s_tables.Count == 0) return;
            s_tables.Clear();
            TablesChanged?.Invoke();
        }

        public static bool TryGetTable(string tableName, out MasterMemoryTableDescriptor table)
        {
            table = s_tables.Find(x => x.TableName == tableName);
            return table != null;
        }

        public static bool TryGetTable(Type recordType, out MasterMemoryTableDescriptor table)
        {
            table = s_tables.Find(x => x.RecordType == recordType);
            return table != null;
        }

        // ------------------------------------------------------------------ customization

        /// <summary>Registers a custom clone function used when an editable copy of a record is created.</summary>
        public static void RegisterCloneProvider<TRecord>(Func<TRecord, TRecord> clone)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            MasterDataCloneUtility.RegisterProvider(typeof(TRecord), clone == null ? (Func<object, object>)null : source => clone((TRecord)source));
        }

        /// <summary>Overrides the display name shown in the record list (also for tables registered by <see cref="RegisterDatabase"/>).</summary>
        public static void SetDisplayName<TRecord>(Func<TRecord, string> getDisplayName)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            if (getDisplayName == null) s_displayNameOverrides.Remove(typeof(TRecord));
            else s_displayNameOverrides[typeof(TRecord)] = record => getDisplayName((TRecord)record);
        }

        /// <summary>Provides the version of the loaded master data. Written to patches and compared when loading.</summary>
        public static void SetMasterVersionProvider(Func<string> provider)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            s_masterVersionProvider = provider;
        }

        public static string GetMasterVersion()
        {
            try
            {
                var version = s_masterVersionProvider?.Invoke();
                return string.IsNullOrEmpty(version) ? UnknownMasterVersion : version;
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Error("Master version provider threw an exception: " + e);
                return UnknownMasterVersion;
            }
        }

        internal static bool HasDisplayNameOverride(Type recordType) => s_displayNameOverrides.ContainsKey(recordType);

        internal static Func<object, string> GetDisplayNameOverride(Type recordType)
        {
            return s_displayNameOverrides.TryGetValue(recordType, out var func) ? func : null;
        }

        // ------------------------------------------------------------------ internals

        static void Add(MasterMemoryTableDescriptor descriptor)
        {
            // one table per name and per record type
            s_tables.RemoveAll(x => x.TableName == descriptor.TableName || x.RecordType == descriptor.RecordType);
            s_tables.Add(descriptor);
            MasterMemoryDebugLog.Info($"Registered table {descriptor.TableName} ({descriptor.RecordType.FullName}).");
            TablesChanged?.Invoke();
            MasterDataPatchAutoLoader.OnTableRegistered(descriptor);
        }

        static MasterMemoryTableDescriptor CreateFromMetaTable(MetaTable metaTable, Func<string, object> getTable)
        {
            var memoryTableName = metaTable.TableName;
            var recordType = metaTable.DataType;
            var tableType = metaTable.TableType;

            var rawDataMethod = tableType.GetMethod("GetRawDataUnsafe", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(tableType.FullName, "GetRawDataUnsafe");
            var selectorProperty = tableType.GetProperty("PrimaryKeySelector", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMemberException(tableType.FullName, "PrimaryKeySelector");
            var keyType = selectorProperty.PropertyType.GetGenericArguments()[1];

            Func<IEnumerable<object>> getAllRecords = () =>
            {
                var table = getTable(memoryTableName);
                if (table == null) return Array.Empty<object>();
                return ToObjects((IEnumerable)rawDataMethod.Invoke(table, null));
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
                // composite key: use the generated selector so the key is exactly the ValueTuple used by FindBy
                Delegate selector = null;
                getPrimaryKey = record =>
                {
                    if (selector == null)
                    {
                        var table = getTable(memoryTableName) ?? throw new InvalidOperationException($"Table '{memoryTableName}' is not loaded.");
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
                CreateDefaultDisplayName(recordType));
        }

        static readonly string[] s_displayNameCandidates = { "Name", "DisplayName", "Title", "Label" };

        /// <summary>Uses a string member called Name / DisplayName / Title / Label, otherwise the first non-key string member.</summary>
        static Func<object, string> CreateDefaultDisplayName(Type recordType)
        {
            var descriptor = MasterDataReflectionCache.Get(recordType);
            MasterMemoryFieldDescriptor field = null;
            foreach (var candidate in s_displayNameCandidates)
            {
                if (descriptor.TryGetField(candidate, out var f) && f.Kind == MasterDataValueKind.String)
                {
                    field = f;
                    break;
                }
            }
            if (field == null) field = descriptor.StringFields.FirstOrDefault(x => !x.IsKey);
            if (field == null) return null;
            return record => field.GetValue(record) as string;
        }

        static IEnumerable<object> ToObjects(IEnumerable source)
        {
            if (source == null) return Array.Empty<object>();
            return source as IEnumerable<object> ?? source.Cast<object>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_tables.Clear();
            s_displayNameOverrides.Clear();
            s_masterVersionProvider = null;
            TablesChanged = null;
        }
    }
}
