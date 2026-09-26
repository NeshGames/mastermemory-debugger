using System;
using System.Collections.Generic;
using System.Reflection;
using MasterMemory;
using MasterMemory.Validation;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Builds a copy of a generated MemoryDatabase with the overrides applied, through MasterMemory's own
    /// <c>ToImmutableBuilder().Diff(records).Build()</c>. Secondary key / range queries and <c>All</c> of the rebuilt
    /// database see the overrides; the original database is never modified.
    /// <code>
    /// // one line in the project: gameplay reads masterService.Database
    /// MasterMemoryDebugRebuild.AutoRebuild(originalDatabase, db =&gt; masterService.Database = db);
    /// </code>
    /// Every rebuild re-sorts the changed tables and creates a new database; records or tables cached elsewhere are not updated.
    /// In release builds nothing is rebuilt and the original database is returned.
    /// </summary>
    public static class MasterMemoryDebugRebuild
    {
        const int MaxReportedFailures = 20;

        /// <summary>
        /// Validate() slower than this stops the validation after every change (it would freeze the game on each edit);
        /// the Validation tab still validates on demand.
        /// </summary>
        internal const double SlowValidateSeconds = 1.0;

        sealed class BuilderInfo
        {
            public MethodInfo ToImmutableBuilder;
            public MethodInfo Build;
            public MethodInfo Validate;
            public Dictionary<Type, MethodInfo> Diff = new Dictionary<Type, MethodInfo>();
        }

        static readonly Dictionary<Type, BuilderInfo> s_builders = new Dictionary<Type, BuilderInfo>();
        static readonly HashSet<Type> s_reportedUnsupported = new HashSet<Type>();

        /// <summary>
        /// Returns a new database built from <paramref name="original"/> and every override whose record type belongs to it,
        /// or <paramref name="original"/> itself when there is nothing to apply.
        /// </summary>
        public static TDatabase Apply<TDatabase>(TDatabase original) where TDatabase : MemoryDatabaseBase
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (!MasterMemoryDebugBuild.IsEnabled || MasterMemoryDebugRuntime.OverrideCount == 0) return original;

            var info = GetInfo(original.GetType());
            if (info.ToImmutableBuilder == null || info.Build == null)
            {
                MasterMemoryDebugLog.Error($"{original.GetType().Name} has no ToImmutableBuilder() / Build(); is it a generated MemoryDatabase?");
                return original;
            }

            var byType = new Dictionary<Type, List<object>>();
            foreach (var entry in MasterMemoryDebugRuntime.GetAllOverrides())
            {
                if (!byType.TryGetValue(entry.Key.RecordType, out var list)) byType.Add(entry.Key.RecordType, list = new List<object>());
                list.Add(entry.Value);
            }

            var builder = info.ToImmutableBuilder.Invoke(original, null);
            var applied = 0;
            foreach (var pair in byType)
            {
                if (!info.Diff.TryGetValue(pair.Key, out var diff))
                {
                    if (s_reportedUnsupported.Add(pair.Key))
                    {
                        MasterMemoryDebugLog.Warning($"Rebuild: {pair.Key.Name} is not a table of {original.GetType().Name}; its overrides are not applied.");
                    }
                    continue;
                }
                var array = Array.CreateInstance(pair.Key, pair.Value.Count);
                for (var i = 0; i < pair.Value.Count; i++) array.SetValue(pair.Value[i], i);
                diff.Invoke(builder, new object[] { array });
                applied++;
            }

            return applied == 0 ? original : (TDatabase)info.Build.Invoke(builder, null);
        }

        /// <summary>
        /// Calls <paramref name="apply"/> now and every time the overrides change with <see cref="Apply{TDatabase}"/>.
        /// With <paramref name="validate"/>, the rebuilt database is checked with MasterMemory's <c>Validate()</c>
        /// (<c>IValidatable&lt;T&gt;</c>): failures that the original database does not have are reported in the log, and
        /// every failure is listed in the debugger's Validation tab (<see cref="MasterMemoryDebugValidation"/>).
        /// Dispose to stop; <paramref name="apply"/> then receives the original database again.
        /// In release builds <paramref name="apply"/> is called once with the original database.
        /// </summary>
        public static IDisposable AutoRebuild<TDatabase>(TDatabase original, Action<TDatabase> apply, bool validate = true) where TDatabase : MemoryDatabaseBase
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (apply == null) throw new ArgumentNullException(nameof(apply));
            return new AutoRebuilder<TDatabase>(original, apply, validate);
        }

        /// <summary>Runs the generated <c>Validate()</c>; null when the database type has none.</summary>
        public static ValidateResult Validate(MemoryDatabaseBase database)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            var method = GetInfo(database.GetType()).Validate;
            return method != null ? (ValidateResult)method.Invoke(database, null) : null;
        }

        /// <summary>Failure messages of <paramref name="result"/> that are not in <paramref name="baseline"/>.</summary>
        internal static List<string> GetNewFailures(ValidateResult result, ValidateResult baseline)
        {
            var known = new HashSet<string>();
            if (baseline != null)
            {
                foreach (var item in baseline.FailedResults) known.Add(item.Message);
            }
            var failures = new List<string>();
            if (result == null) return failures;
            foreach (var item in result.FailedResults)
            {
                if (!known.Contains(item.Message)) failures.Add(item.Message);
            }
            return failures;
        }

        static BuilderInfo GetInfo(Type databaseType)
        {
            if (s_builders.TryGetValue(databaseType, out var info)) return info;

            info = new BuilderInfo
            {
                ToImmutableBuilder = databaseType.GetMethod("ToImmutableBuilder", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
                Validate = databaseType.GetMethod("Validate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null),
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
            }
            s_builders.Add(databaseType, info);
            return info;
        }

        sealed class AutoRebuilder<TDatabase> : IDisposable, MasterMemoryDebugValidation.ISource where TDatabase : MemoryDatabaseBase
        {
            readonly TDatabase original;
            readonly Action<TDatabase> apply;
            readonly bool validate;
            TDatabase current;
            ValidateResult baseline;
            ValidateResult currentResult;
            int newFailureCount;
            bool disposed;
            // false after a slow Validate(): only the Validation tab validates
            bool validateOnChange = true;

            public AutoRebuilder(TDatabase original, Action<TDatabase> apply, bool validate)
            {
                this.original = original;
                this.apply = apply;
                this.validate = validate;
                current = original;
                if (MasterMemoryDebugBuild.IsEnabled) MasterMemoryDebugRuntime.OverridesChanged += Rebuild;
                Rebuild();
                if (validate) MasterMemoryDebugValidation.Add(this);
            }

            public int NewFailureCount => newFailureCount;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (MasterMemoryDebugBuild.IsEnabled) MasterMemoryDebugRuntime.OverridesChanged -= Rebuild;
                MasterMemoryDebugValidation.Remove(this);
                apply(original);
            }

            /// <summary>Failures of the current database; validates only what the rebuilds did not validate yet.</summary>
            public void Collect(List<MasterMemoryValidationFailure> failures)
            {
                try
                {
                    baseline ??= Validate(original);
                    var result = ReferenceEquals(current, original) ? baseline : currentResult ??= Validate(current);
                    MasterMemoryDebugValidation.Convert(result, baseline, failures);
                }
                catch (Exception e)
                {
                    MasterMemoryDebugLog.Warning("Validate threw: " + (e.InnerException ?? e).Message);
                }
            }

            void Rebuild()
            {
                TDatabase database;
                try
                {
                    database = Apply(original);
                }
                catch (Exception e)
                {
                    MasterMemoryDebugLog.Error("Rebuild failed, the original database is used: " + (e.InnerException ?? e).Message);
                    database = original;
                }
                current = database;
                currentResult = null;
                newFailureCount = 0;
                apply(database);
                if (!validate) return;
                if (validateOnChange && !ReferenceEquals(database, original)) Report(database);
                MasterMemoryDebugValidation.NotifyChanged();
            }

            void Report(TDatabase database)
            {
                try
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    baseline ??= Validate(original);
                    currentResult = Validate(database);
                    watch.Stop();
                    if (watch.Elapsed.TotalSeconds > SlowValidateSeconds)
                    {
                        validateOnChange = false;
                        MasterMemoryDebugLog.Warning(
                            $"Validate: MasterMemory Validate() took {watch.Elapsed.TotalSeconds:0.0} s, so the database is no longer validated after every change. " +
                            "Use the Validate button of the Validation tab (MasterMemory compiles the Exists() expressions for every record; large tables are slow).");
                    }
                    var failures = GetNewFailures(currentResult, baseline);
                    newFailureCount = failures.Count;
                    for (var i = 0; i < failures.Count && i < MaxReportedFailures; i++)
                    {
                        MasterMemoryDebugLog.Warning("Validate: " + failures[i]);
                    }
                    if (failures.Count > MaxReportedFailures)
                    {
                        MasterMemoryDebugLog.Warning($"Validate: {failures.Count - MaxReportedFailures} more failures");
                    }
                }
                catch (Exception e)
                {
                    MasterMemoryDebugLog.Warning("Validate threw: " + (e.InnerException ?? e).Message);
                }
            }
        }
    }
}
