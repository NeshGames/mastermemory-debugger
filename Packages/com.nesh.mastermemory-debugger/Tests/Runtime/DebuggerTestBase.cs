using NUnit.Framework;
using Nesh.MasterMemoryDebugger.Tests.Generated;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger.Tests
{
    /// <summary>Registers the test database and resets every static state of the debugger around each test.</summary>
    public abstract class DebuggerTestBase
    {
        protected MemoryDatabase Database { get; private set; }
        protected MasterMemoryDebuggerSettings Settings { get; private set; }

        MasterMemoryDebuggerSettings previousSettings;

        [SetUp]
        public void BaseSetUp()
        {
            previousSettings = MasterMemoryDebuggerSettings.Current;
            Settings = ScriptableObject.CreateInstance<MasterMemoryDebuggerSettings>();
            Settings.AutoLoadPatch = false;
            Settings.LogLevel = MasterMemoryDebugLogLevel.None;
            Settings.DefaultPatchName = "unit-test";
            MasterMemoryDebuggerSettings.SetCurrent(Settings);

            ResetState();
            Database = TestData.CreateDatabase();
        }

        [TearDown]
        public void BaseTearDown()
        {
            ResetState();
            MasterMemoryDebugLocalization.EndTests();
            MasterTablePins.EndTests();
            MasterMemoryDebuggerSettings.SetCurrent(previousSettings);
            Object.DestroyImmediate(Settings);
        }

        protected void RegisterTestDatabase()
        {
            var db = Database;
            MasterMemoryDebugRegistry.RegisterDatabase(MemoryDatabase.GetMetaDatabase(), name => MemoryDatabase.GetTable(db, name));
        }

        protected static MasterMemoryTableDescriptor Table<T>()
        {
            Assert.IsTrue(MasterMemoryDebugRegistry.TryGetTable(typeof(T), out var table), typeof(T).Name + " is not registered");
            return table;
        }

        static void ResetState()
        {
            MasterMemoryDebugRuntime.ClearAllOverrides();
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(null);
            MasterMemoryDebugRegistry.ClearTableGroups();
            MasterMemoryDebugRegistry.SetDisplayName<ManualItem>(null);
            MasterDataCloneUtility.ClearProviders();
            MasterMemoryReferences.ClearCache();
            MasterGridLayout.ClearSettings();
            MasterMemoryDebugLocalization.ResetForTests();
            MasterTablePins.ResetForTests();
        }
    }
}
