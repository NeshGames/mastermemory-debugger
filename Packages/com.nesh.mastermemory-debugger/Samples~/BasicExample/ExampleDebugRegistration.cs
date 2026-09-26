namespace Nesh.MasterMemoryDebugger.Samples.BasicExample
{
    /// <summary>Registers the sample tables to the debugger. All calls are no-ops outside the Editor / Development Builds.</summary>
    public static class ExampleDebugRegistration
    {
        public static void Register()
        {
            // 1. version first: it is written to patches and checked when a patch is (auto) loaded
            MasterMemoryDebugRegistry.SetMasterVersionProvider(() => ExampleDatabaseBootstrap.MasterVersion);

            // 2. every table of the generated database in one call.
            //    The delegate must return tables of the ORIGINAL database.
            MasterMemoryDebugRegistry.RegisterDatabase(
                MemoryDatabase.GetMetaDatabase(),
                tableName => MemoryDatabase.GetTable(ExampleDatabaseBootstrap.OriginalDatabase, tableName));

            // 3. optional customization
            MasterMemoryDebugRegistry.SetDisplayName<ExampleEnemyLevelMaster>(x => $"Enemy {x.EnemyId} Lv.{x.Level}");

            // Manual registration of a single table is also possible:
            // MasterMemoryDebugRegistry.RegisterTable<ExampleItemMaster, int>(
            //     "ExampleItemMaster",
            //     () => ExampleDatabaseBootstrap.OriginalDatabase.ExampleItemMasterTable.All,
            //     x => x.Id,
            //     x => x.Name);

            // A custom clone function (the default shallow clone is enough for most records):
            // MasterMemoryDebugRegistry.RegisterCloneProvider<ExampleItemMaster>(x => x with { });
        }
    }
}
