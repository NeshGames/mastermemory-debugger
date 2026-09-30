using NUnit.Framework;
using Nesh.MasterMemoryDebugger.Tests.Generated;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger.Tests
{
    /// <summary>
    /// Registers the test database and resets every static state of the debugger around each test. Like a game at startup,
    /// every test starts with the <see cref="TestFixedConverter"/> registered and no table.
    /// </summary>
    public abstract class DebuggerTestBase
    {
        protected MemoryDatabase Database { get; private set; }
        protected MasterMemoryDebuggerSettings Settings { get; private set; }

        /// <summary>The token of the <see cref="TestFixedConverter"/> registration.</summary>
        protected System.IDisposable FixedConverter { get; private set; }

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
            FixedConverter = MasterDataValueConverters.Register(new TestFixedConverter());
            Database = TestData.CreateDatabase();
        }

        [TearDown]
        public void BaseTearDown()
        {
            ResetState();
            MasterMemoryDebugLocalization.EndTests();
            MasterTablePins.EndTests();
            MasterGridLayout.EndTests();
            MasterSearchHistory.EndTests();
            MasterSavedViews.EndTests();
            MasterMemoryDebuggerSettings.SetCurrent(previousSettings);
            Object.DestroyImmediate(Settings);
        }

        protected void RegisterTestDatabase()
        {
            var db = Database;
            MasterMemoryDebugRegistry.RegisterDatabase(MemoryDatabase.GetMetaDatabase(), name => MemoryDatabase.GetTable(db, name));
        }

        /// <summary>Registers <see cref="TestData.CreateTunings"/> as the table "TestTuning".</summary>
        protected static TestTuning[] RegisterTunings()
        {
            var tunings = TestData.CreateTunings();
            MasterMemoryDebugRegistry.RegisterTable<TestTuning, int>(nameof(TestTuning), () => tunings, x => x.Id);
            return tunings;
        }

        protected static MasterMemoryTableDescriptor Table<T>()
        {
            Assert.IsTrue(MasterMemoryDebugRegistry.TryGetTable(typeof(T), out var table), typeof(T).Name + " is not registered");
            return table;
        }

        static void ResetState()
        {
            MasterMemoryDebugRuntime.ClearAllOverrides();
            MasterMemoryDebugHistory.Clear();
            MasterMemoryDebugRegistry.ClearTables();
            MasterMemoryDebugRegistry.SetMasterVersionProvider(null);
            MasterMemoryDebugRegistry.ClearTableGroups();
            MasterMemoryDebugRegistry.SetDisplayName<ManualItem>(null);
            MasterDataCloneUtility.ClearProviders();
            MasterMemoryReferences.Reset();
            MasterMemoryDebugValidation.ClearForTests();
            MasterGridLayout.ResetForTests();
            MasterMemoryDebugLocalization.ResetForTests();
            MasterTablePins.ResetForTests();
            MasterSearchHistory.ResetForTests();
            MasterDataValueConverters.ResetForTests();
            MasterSavedViews.ResetForTests();
        }
    }
}
