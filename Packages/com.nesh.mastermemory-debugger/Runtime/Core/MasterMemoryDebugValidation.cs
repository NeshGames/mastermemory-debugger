using System;
using System.Collections.Generic;
using MasterMemory.Validation;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A failure of MasterMemory's <c>Validate()</c> (<c>IValidatable&lt;T&gt;</c>).</summary>
    public sealed class MasterMemoryValidationFailure
    {
        public MasterMemoryValidationFailure(Type recordType, string message, object record, bool isNew)
        {
            RecordType = recordType;
            Message = message ?? string.Empty;
            Record = record;
            IsNew = isNew;
        }

        public Type RecordType { get; }

        public string Message { get; }

        /// <summary>The failing record (of the rebuilt database); null when MasterMemory does not report one.</summary>
        public object Record { get; }

        /// <summary>True when the original database does not have this failure: the overrides caused it.</summary>
        public bool IsNew { get; }

        public override string ToString() => $"{RecordType?.Name}: {Message}";
    }

    /// <summary>
    /// Validation results of the databases rebuilt with <see cref="MasterMemoryDebugRebuild.AutoRebuild{TDatabase}"/>
    /// (validate: true), shown by the debugger's Validation tab. <see cref="Run"/> validates the current databases;
    /// failures that the original databases do not have are marked <see cref="MasterMemoryValidationFailure.IsNew"/>.
    /// </summary>
    public static class MasterMemoryDebugValidation
    {
        internal interface ISource
        {
            /// <summary>New failures found by the last rebuild, without validating again.</summary>
            int NewFailureCount { get; }

            void Collect(List<MasterMemoryValidationFailure> failures);
        }

        static readonly List<ISource> s_sources = new List<ISource>();

        /// <summary>Raised after a rebuild validated the database, and when a database is added or removed.</summary>
        public static event Action Changed;

        /// <summary>False when no database is validated (AutoRebuild is not used, or validate: false).</summary>
        public static bool IsAvailable => s_sources.Count > 0;

        /// <summary>Failures caused by the overrides, as found by the last rebuilds (cheap, no validation is run).</summary>
        public static int NewFailureCount
        {
            get
            {
                var count = 0;
                foreach (var source in s_sources) count += source.NewFailureCount;
                return count;
            }
        }

        /// <summary>Every failure of the current databases, new ones first.</summary>
        public static List<MasterMemoryValidationFailure> Run()
        {
            var failures = new List<MasterMemoryValidationFailure>();
            if (!MasterMemoryDebugBuild.IsEnabled) return failures;
            foreach (var source in s_sources.ToArray()) source.Collect(failures);
            // stable: new failures first, otherwise in MasterMemory's order
            var ordered = new List<MasterMemoryValidationFailure>(failures.Count);
            foreach (var failure in failures) if (failure.IsNew) ordered.Add(failure);
            foreach (var failure in failures) if (!failure.IsNew) ordered.Add(failure);
            return ordered;
        }

        /// <summary>Converts <paramref name="result"/>; failures whose message is not in <paramref name="baseline"/> are new.</summary>
        internal static void Convert(ValidateResult result, ValidateResult baseline, List<MasterMemoryValidationFailure> failures)
        {
            if (result == null) return;
            var compare = baseline != null && !ReferenceEquals(result, baseline);
            var known = new HashSet<string>();
            if (compare)
            {
                foreach (var item in baseline.FailedResults) known.Add(item.Message);
            }
            foreach (var item in result.FailedResults)
            {
                failures.Add(new MasterMemoryValidationFailure(item.Type, item.Message, item.Data, compare && !known.Contains(item.Message)));
            }
        }

        internal static void Add(ISource source)
        {
            if (!MasterMemoryDebugBuild.IsEnabled || s_sources.Contains(source)) return;
            s_sources.Add(source);
            Changed?.Invoke();
        }

        internal static void Remove(ISource source)
        {
            if (s_sources.Remove(source)) Changed?.Invoke();
        }

        internal static void NotifyChanged() => Changed?.Invoke();

        internal static void ClearForTests() => s_sources.Clear();
    }
}
