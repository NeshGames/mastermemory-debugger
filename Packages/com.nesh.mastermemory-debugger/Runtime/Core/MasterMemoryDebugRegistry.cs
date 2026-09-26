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

        /// <summary>Group name of tables without an assigned group (only shown when groups are used).</summary>
        public const string UngroupedName = "Other";

        static readonly List<MasterMemoryTableDescriptor> s_tables = new List<MasterMemoryTableDescriptor>();
        static readonly Dictionary<Type, Func<object, string>> s_displayNameOverrides = new Dictionary<Type, Func<object, string>>();
        static Func<string> s_masterVersionProvider;
        static readonly List<string> s_groupOrder = new List<string>();
        static readonly Dictionary<string, string> s_groupByTableName = new Dictionary<string, string>(StringComparer.Ordinal);
        static readonly Dictionary<Type, string> s_groupByRecordType = new Dictionary<Type, string>();

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

        // ------------------------------------------------------------------ groups

        /// <summary>
        /// Puts tables into a group of the table list, for example
        /// <c>SetTableGroup("Battle", "CharacterMaster", "MonsterMaster", "SkillMaster", "EffectMaster")</c>.
        /// A name matches the registered table name (e.g. "SkillMaster") or the [MemoryTable] name (e.g. "skill").
        /// Groups are listed in the order they are first set; tables without a group go to "Other".
        /// May be called before or after the tables are registered.
        /// </summary>
        public static void SetTableGroup(string groupName, params string[] tableNames)
        {
            if (!MasterMemoryDebugBuild.IsEnabled || tableNames == null) return;
            groupName = NormalizeGroupName(groupName);
            foreach (var tableName in tableNames)
            {
                if (string.IsNullOrEmpty(tableName)) continue;
                if (groupName == null) s_groupByTableName.Remove(tableName);
                else s_groupByTableName[tableName] = groupName;
            }
            AddGroupOrder(groupName);
            TablesChanged?.Invoke();
        }

        /// <summary>Puts the table of <typeparamref name="TRecord"/> into a group. Null removes the assignment.</summary>
        public static void SetTableGroup<TRecord>(string groupName)
        {
            if (!MasterMemoryDebugBuild.IsEnabled) return;
            groupName = NormalizeGroupName(groupName);
            if (groupName == null) s_groupByRecordType.Remove(typeof(TRecord));
            else s_groupByRecordType[typeof(TRecord)] = groupName;
            AddGroupOrder(groupName);
            TablesChanged?.Invoke();
        }

        /// <summary>Removes every group assignment.</summary>
        public static void ClearTableGroups()
        {
            if (s_groupOrder.Count == 0 && s_groupByTableName.Count == 0 && s_groupByRecordType.Count == 0) return;
            s_groupOrder.Clear();
            s_groupByTableName.Clear();
            s_groupByRecordType.Clear();
            TablesChanged?.Invoke();
        }

        /// <summary>Assigned group of a table, or null.</summary>
        public static string GetTableGroup(MasterMemoryTableDescriptor table)
        {
            if (table == null) return null;
            if (s_groupByRecordType.TryGetValue(table.RecordType, out var group)) return group;
            if (s_groupByTableName.TryGetValue(table.TableName, out group)) return group;
            if (table.MemoryTableName != null && s_groupByTableName.TryGetValue(table.MemoryTableName, out group)) return group;
            return null;
        }

        /// <summary>
        /// Registered tables grouped for display: groups in the order they were first set, tables sorted by name,
        /// ungrouped tables last in <see cref="UngroupedName"/>. Empty groups are omitted.
        /// When no table has a group, a single ungrouped group is returned.
        /// </summary>
        public static List<MasterMemoryTableGroup> GetGroupedTables()
        {
            var groups = new List<MasterMemoryTableGroup>();
            var byName = new Dictionary<string, MasterMemoryTableGroup>(StringComparer.Ordinal);
            foreach (var name in s_groupOrder)
            {
                var group = new MasterMemoryTableGroup(name, false);
                groups.Add(group);
                byName.Add(name, group);
            }
            var ungrouped = new MasterMemoryTableGroup(UngroupedName, true);

            foreach (var table in s_tables)
            {
                var name = GetTableGroup(table);
                (name != null && byName.TryGetValue(name, out var group) ? group : ungrouped).Tables.Add(table);
            }

            groups.Add(ungrouped);
            groups.RemoveAll(x => x.Tables.Count == 0);
            foreach (var group in groups) group.Tables.Sort((a, b) => string.CompareOrdinal(a.TableName, b.TableName));
            return groups;
        }

        static string NormalizeGroupName(string groupName)
        {
            groupName = groupName?.Trim();
            return string.IsNullOrEmpty(groupName) ? null : groupName;
        }

        static void AddGroupOrder(string groupName)
        {
            if (groupName != null && !s_groupOrder.Contains(groupName)) s_groupOrder.Add(groupName);
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
            s_groupOrder.Clear();
            s_groupByTableName.Clear();
            s_groupByRecordType.Clear();
            TablesChanged = null;
        }
    }
}
