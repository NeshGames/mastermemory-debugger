using System;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger.Samples.BasicExample
{
    /// <summary>
    /// OPTIONAL, project side: rebuilds the gameplay database whenever overrides change, using MasterMemory's
    /// official ImmutableBuilder. Afterwards secondary key / range queries and <c>All</c> also see the overrides.
    /// <para>
    /// Trade-offs: every change rebuilds and re-sorts the whole database; references to the previous database or
    /// records held elsewhere are not updated; one Diff call per table has to be written by hand.
    /// The debugger package itself never rebuilds or swaps a database.
    /// </para>
    /// </summary>
    public sealed class ExampleDatabaseRebuilder : IDisposable
    {
        readonly ExampleMasterDataService service;

        public ExampleDatabaseRebuilder(ExampleMasterDataService service)
        {
            this.service = service;
            MasterMemoryDebugRuntime.OverridesChanged += Rebuild;
            Rebuild();
        }

        public void Dispose()
        {
            MasterMemoryDebugRuntime.OverridesChanged -= Rebuild;
            service.Database = ExampleDatabaseBootstrap.OriginalDatabase;
        }

        void Rebuild()
        {
            var original = ExampleDatabaseBootstrap.OriginalDatabase;
            if (MasterMemoryDebugRuntime.OverrideCount == 0)
            {
                service.Database = original;
                return;
            }

            // always start from the ORIGINAL database so that Reset restores the original values
            var builder = original.ToImmutableBuilder();
            builder.Diff(MasterMemoryDebugRuntime.GetOverrides<ExampleSkillMaster>());
            builder.Diff(MasterMemoryDebugRuntime.GetOverrides<ExampleItemMaster>());
            builder.Diff(MasterMemoryDebugRuntime.GetOverrides<ExampleEnemyLevelMaster>());
            builder.Diff(MasterMemoryDebugRuntime.GetOverrides<ExampleCharacterMaster>());
            builder.Diff(MasterMemoryDebugRuntime.GetOverrides<ExampleEffectMaster>());
            builder.Diff(MasterMemoryDebugRuntime.GetOverrides<ExampleShopMaster>());
            builder.Diff(MasterMemoryDebugRuntime.GetOverrides<ExampleGameConfigMaster>());
            service.Database = builder.Build();
            Debug.Log($"[Example] Gameplay database rebuilt with {MasterMemoryDebugRuntime.OverrideCount} overrides.");
        }
    }
}
