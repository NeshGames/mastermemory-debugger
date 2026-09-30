using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using MasterMemory;
using MasterMemory.Validation;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>A member that holds a key of another table, e.g. <c>Character.StartSkillId -&gt; Skill.Id</c>.</summary>
    public sealed class MasterMemoryReference
    {
        public MasterMemoryReference(Type sourceType, string sourceMember, Type targetType, string targetMember)
        {
            SourceType = sourceType;
            SourceMember = sourceMember;
            TargetType = targetType;
            TargetMember = targetMember;
        }

        public Type SourceType { get; }

        /// <summary>Member of the source record; may be a path such as <c>Reward.ItemId</c>.</summary>
        public string SourceMember { get; }

        public Type TargetType { get; }

        public string TargetMember { get; }

        public override string ToString() => $"{SourceType.Name}.{SourceMember} -> {TargetType.Name}.{TargetMember}";
    }

    /// <summary>
    /// References declared with MasterMemory validation:
    /// <c>validator.GetReferenceSet&lt;Skill&gt;().Exists(x =&gt; x.StartSkillId, y =&gt; y.Id)</c> in <c>IValidatable&lt;T&gt;.Validate</c>.
    /// They are found by running Validate on some records with empty reference tables and reading the member names
    /// of each Exists() from its failure. Records without IValidatable simply have no references.
    /// </summary>
    public static class MasterMemoryReferences
    {
        /// <summary>Records per table whose Validate is run (branches in Validate may depend on the record).</summary>
        const int MaxScannedRecords = 200;

        static readonly Regex s_exists = new Regex(@"^Exists failed: (\S+) -> (\S+), value =", RegexOptions.CultureInvariant);
        static readonly Dictionary<Type, IReadOnlyList<MasterMemoryReference>> s_cache = new Dictionary<Type, IReadOnlyList<MasterMemoryReference>>();
        static readonly Dictionary<Type, List<MasterMemoryReference>> s_explicit =
            new Dictionary<Type, List<MasterMemoryReference>>();

        /// <summary>
        /// Registers a reference explicitly. Explicit references are stable across MasterMemory validation message changes;
        /// automatic IValidatable/Exists discovery remains as a fallback and is merged with them.
        /// </summary>
        public static void Register<TSource, TTarget, TValue>(
            Expression<Func<TSource, TValue>> source,
            Expression<Func<TTarget, TValue>> target)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (target == null) throw new ArgumentNullException(nameof(target));
            var reference = new MasterMemoryReference(
                typeof(TSource), MemberPath(source), typeof(TTarget), MemberPath(target));
            if (!s_explicit.TryGetValue(typeof(TSource), out var list))
                s_explicit.Add(typeof(TSource), list = new List<MasterMemoryReference>());
            if (!list.Exists(x => x.SourceMember == reference.SourceMember
                && x.TargetType == reference.TargetType && x.TargetMember == reference.TargetMember))
                list.Add(reference);
            s_cache.Remove(typeof(TSource));
        }

        /// <summary>References from the records of <paramref name="table"/> (cached per record type).</summary>
        public static IReadOnlyList<MasterMemoryReference> Get(MasterMemoryTableDescriptor table)
        {
            if (table == null) return Array.Empty<MasterMemoryReference>();
            if (s_cache.TryGetValue(table.RecordType, out var cached)) return cached;

            IReadOnlyList<MasterMemoryReference> result;
            try
            {
                var records = new List<object>();
                foreach (var record in table.CreateRecordSnapshot())
                {
                    if (record.IsAdded) break;
                    records.Add(record.Original);
                    if (records.Count >= MaxScannedRecords) break;
                }
                var discovered = Discover(table.RecordType, records);
                if (!s_explicit.TryGetValue(table.RecordType, out var explicitReferences) || explicitReferences.Count == 0)
                {
                    result = discovered;
                }
                else
                {
                    var merged = new List<MasterMemoryReference>(explicitReferences);
                    foreach (var reference in discovered)
                    {
                        if (!merged.Exists(x => x.SourceMember == reference.SourceMember
                            && x.TargetType == reference.TargetType && x.TargetMember == reference.TargetMember))
                            merged.Add(reference);
                    }
                    result = merged;
                }
            }
            catch (Exception e)
            {
                MasterMemoryDebugLog.Warning($"Could not read the references of {table.TableName}: {(e.InnerException ?? e).Message}");
                result = s_explicit.TryGetValue(table.RecordType, out var explicitReferences)
                    ? new List<MasterMemoryReference>(explicitReferences)
                    : (IReadOnlyList<MasterMemoryReference>)Array.Empty<MasterMemoryReference>();
            }
            s_cache[table.RecordType] = result;
            return result;
        }

        /// <summary>The reference of <paramref name="member"/>, or null.</summary>
        public static MasterMemoryReference Find(MasterMemoryTableDescriptor table, string member)
        {
            foreach (var reference in Get(table))
            {
                if (reference.SourceMember == member) return reference;
            }
            return null;
        }

        /// <summary>References into <paramref name="target"/> from the registered tables (including itself).</summary>
        public static List<MasterMemoryReference> GetIncoming(MasterMemoryTableDescriptor target)
        {
            var result = new List<MasterMemoryReference>();
            if (target == null) return result;
            foreach (var table in MasterMemoryDebugRegistry.Tables)
            {
                foreach (var reference in Get(table))
                {
                    if (reference.TargetType == target.RecordType) result.Add(reference);
                }
            }
            return result;
        }

        /// <summary>
        /// Records of the source table of <paramref name="reference"/> whose member holds <paramref name="value"/>
        /// (current values, overrides and added records included, deleted records left out). Empty when the member is not a
        /// direct member of the record.
        /// </summary>
        public static List<MasterMemoryRecordDescriptor> FindReferencing(MasterMemoryReference reference, object value)
        {
            var result = new List<MasterMemoryRecordDescriptor>();
            if (reference == null || value == null) return result;
            if (!MasterMemoryDebugRegistry.TryGetTable(reference.SourceType, out var source)) return result;
            if (!source.TypeDescriptor.TryGetField(reference.SourceMember, out var field)) return result;
            foreach (var record in source.CreateRecordSnapshot())
            {
                var current = record.Current;
                if (current == null || record.IsDeleted) continue;
                if (SameValue(field.GetValue(current), value)) result.Add(record);
            }
            return result;
        }

        /// <summary>The value of <paramref name="record"/> that records referencing it hold, or null.</summary>
        public static object GetReferencedValue(MasterMemoryReference reference, MasterMemoryRecordDescriptor record)
        {
            if (reference == null || record == null) return null;
            var current = record.Current;
            return current != null && record.Table.TypeDescriptor.TryGetField(reference.TargetMember, out var field) ? field.GetValue(current) : null;
        }

        // int and long keys of the same value (Exists compares converted values)
        static bool SameValue(object a, object b)
        {
            if (Equals(a, b)) return true;
            if (a == null || b == null || a.GetType() == b.GetType()) return false;
            return MasterDataValueUtility.Format(a) == MasterDataValueUtility.Format(b);
        }

        internal static void ClearCache() => s_cache.Clear();

        internal static void Reset()
        {
            s_cache.Clear();
            s_explicit.Clear();
        }

        static string MemberPath(LambdaExpression expression)
        {
            Expression node = expression.Body;
            if (node is UnaryExpression unary
                && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
                node = unary.Operand;

            var names = new Stack<string>();
            while (node is MemberExpression member)
            {
                names.Push(member.Member.Name);
                node = member.Expression;
            }
            if (!(node is ParameterExpression) || names.Count == 0)
                throw new ArgumentException("Reference expressions must be direct member paths.", nameof(expression));
            return string.Join(".", names);
        }

        internal static IReadOnlyList<MasterMemoryReference> Discover(Type recordType, IEnumerable<object> records)
        {
            var validatable = typeof(IValidatable<>).MakeGenericType(recordType);
            if (!validatable.IsAssignableFrom(recordType)) return Array.Empty<MasterMemoryReference>();

            var validate = validatable.GetMethod(nameof(IValidatable<object>.Validate));
            var collector = new List<MasterMemoryReference>();
            var validatorType = typeof(RecordingValidator<>).MakeGenericType(recordType);
            foreach (var record in records)
            {
                if (record == null) continue;
                var validator = Activator.CreateInstance(validatorType, record);
                try
                {
                    validate.Invoke(record, new[] { validator });
                }
                catch (Exception)
                {
                    // Validate code of the project may throw on unexpected data; keep what was found
                }
                ((IRecordingValidator)validator).Collect(collector);
            }
            return collector;
        }

        static void Add(List<MasterMemoryReference> collector, Type sourceType, Type targetType, ValidateResult result)
        {
            foreach (var failure in result.FailedResults)
            {
                var match = s_exists.Match(failure.Message ?? string.Empty);
                if (!match.Success) continue;
                var reference = new MasterMemoryReference(
                    sourceType, StripOwner(match.Groups[1].Value, sourceType),
                    targetType, StripOwner(match.Groups[2].Value, targetType));
                if (!collector.Exists(x => x.SourceMember == reference.SourceMember && x.TargetType == targetType && x.TargetMember == reference.TargetMember))
                {
                    collector.Add(reference);
                }
            }
        }

        /// <summary><c>Skill.Reward.ItemId</c> → <c>Reward.ItemId</c>.</summary>
        static string StripOwner(string path, Type owner)
        {
            if (path.StartsWith(owner.Name + ".", StringComparison.Ordinal)) return path.Substring(owner.Name.Length + 1);
            var dot = path.IndexOf('.');
            return dot >= 0 ? path.Substring(dot + 1) : path;
        }

        interface IRecordingValidator
        {
            void Collect(List<MasterMemoryReference> collector);
        }

        /// <summary>Runs Validate against empty tables, so that every Exists() fails and reports its members.</summary>
        // created through reflection only
        [UnityEngine.Scripting.Preserve]
        sealed class RecordingValidator<T> : IValidator<T>, IRecordingValidator
        {
            // MasterMemory formats the primary key into the failure message
            static readonly Func<T, object> s_noKey = _ => string.Empty;

            readonly T item;
            readonly List<(Type target, ValidateResult result)> references = new List<(Type, ValidateResult)>();

            [UnityEngine.Scripting.Preserve]
            public RecordingValidator(T item)
            {
                this.item = item;
            }

            public void Collect(List<MasterMemoryReference> collector)
            {
                foreach (var (target, result) in references) Add(collector, typeof(T), target, result);
            }

            public ValidatableSet<T> GetTableSet() => new ValidatableSet<T>(Array.Empty<T>(), new ValidateResult(), "PK", s_noKey);

            public ReferenceSet<T, TRef> GetReferenceSet<TRef>()
            {
                var result = new ValidateResult();
                references.Add((typeof(TRef), result));
                return new ReferenceSet<T, TRef>(item, Array.Empty<TRef>(), result, "PK", s_noKey);
            }

            public void Validate(Expression<Func<T, bool>> predicate)
            {
            }

            public void Validate(Func<T, bool> predicate, string message)
            {
            }

            public void ValidateAction(Expression<Func<bool>> predicate)
            {
            }

            public void ValidateAction(Func<bool> predicate, string message)
            {
            }

            public void Fail(string message)
            {
            }

            // table-wide checks (Unique / Sequential) never hold references
            public bool CallOnce() => false;
        }
    }
}
