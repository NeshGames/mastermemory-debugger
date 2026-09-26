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

            // 3. optional: groups of the table list (folders). Names are the registered table names
            //    (the record class name for RegisterDatabase) or the [MemoryTable] names.
            //    Groups are listed in this order; tables without a group are listed under "Other".
            MasterMemoryDebugRegistry.SetTableGroup("Battle",
                nameof(ExampleCharacterMaster),
                nameof(ExampleEnemyLevelMaster),
                nameof(ExampleSkillMaster),
                nameof(ExampleEffectMaster),
                nameof(ExampleItemMaster),
                nameof(ExampleWeaponMaster));
            // wide test tables
            MasterMemoryDebugRegistry.SetTableGroup("Test", nameof(ExampleManyColumnsMaster), nameof(ExampleLargeMaster));
            MasterMemoryDebugRegistry.SetTableGroup("Economy", nameof(ExampleShopMaster));
            // or by type: MasterMemoryDebugRegistry.SetTableGroup<ExampleShopMaster>("Economy");
            // ExampleGameConfigMaster has no group -> "Other"

            // 4. optional customization
            MasterMemoryDebugRegistry.SetDisplayName<ExampleEnemyLevelMaster>(x => $"Enemy {x.EnemyId} Lv.{x.Level}");
            MasterMemoryDebugRegistry.SetDisplayName<ExampleShopMaster>(x =>
                ExampleDatabaseBootstrap.OriginalDatabase.ExampleItemMasterTable.FindById(x.ItemId).Name);

            // 5. optional: labels and tips per language (header dropdown switches between the code names and each language).
            //    Tables / fields are matched by registered table name (or [MemoryTable] name) and member name.
            MasterMemoryDebugLocalization.SetTableLabel<ExampleSkillMaster>("zh-TW", "技能", "所有技能的基本數值");
            MasterMemoryDebugLocalization.SetTableLabel<ExampleWeaponMaster>("zh-TW", "武器", "寬表格範例：27 個欄位、300 筆資料");
            MasterMemoryDebugLocalization.SetTableLabel<ExampleCharacterMaster>("zh-TW", "角色");
            MasterMemoryDebugLocalization.SetTableLabel<ExampleItemMaster>("zh-TW", "道具");
            MasterMemoryDebugLocalization.SetFieldLabel<ExampleSkillMaster>("Name", "zh-TW", "名稱");
            MasterMemoryDebugLocalization.SetFieldLabel<ExampleSkillMaster>("Damage", "zh-TW", "傷害", "基礎傷害，未含角色加成");
            MasterMemoryDebugLocalization.SetFieldLabel<ExampleSkillMaster>("Cooldown", "zh-TW", "冷卻", "單位：秒");
            MasterMemoryDebugLocalization.SetFieldLabel<ExampleSkillMaster>("Element", "zh-TW", "屬性");
            // a tip without a language is shown in every language (and with the code names)
            MasterMemoryDebugLocalization.SetFieldLabel<ExampleSkillMaster>("EffectIds", null, null, "IDs of ExampleEffectMaster applied on hit");
            // many labels at once, e.g. from a spreadsheet exported as tab separated text (table, field, language, label, tip):
            MasterMemoryDebugLocalization.LoadTsv(
                "table\tfield\tlanguage\tlabel\ttip\n" +
                "ExampleWeaponMaster\tRarity\tzh-TW\t稀有度\t\n" +
                "ExampleWeaponMaster\tAttack\tzh-TW\t攻擊力\t物理攻擊\n" +
                "ExampleWeaponMaster\tCriticalRate\tzh-TW\t爆擊率\t0 到 1\n" +
                "ExampleWeaponMaster\tPrice\tzh-TW\t價格\t商店售價\n");

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
