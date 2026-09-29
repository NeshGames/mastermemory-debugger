using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Makes the members of one value type the debugger does not know (a fixed-point number, a typed id, ...) editable:
    /// shown, typed, searched, batch edited and written to patches as text. Register it with
    /// <see cref="MasterDataValueConverters.Register"/> before the tables; derive from
    /// <see cref="MasterDataValueConverter{T}"/> rather than implementing this directly.
    /// <para>
    /// Optional capabilities, implemented by the same object: <see cref="IComparer"/> orders the values (sorting a column,
    /// <c>&gt; &gt;= &lt; &lt;=</c> in queries) and <see cref="IMasterDataValueDrawer"/> replaces the default text editor.
    /// Equality is the type's own <see cref="object.Equals(object)"/>.
    /// </para>
    /// </summary>
    public interface IMasterDataValueConverter
    {
        /// <summary>The converted type: a closed type that is not a simple value, <see cref="Nullable{T}"/>, array or list.</summary>
        Type ValueType { get; }

        /// <summary>The canonical text of a value: <c>TryParse(Format(v))</c> returns a value equal to <c>v</c>.</summary>
        string Format(object value);

        /// <summary>Reads a value typed by a user (inspector, Batch Edit, TSV import, queries). Never throws for bad text.</summary>
        bool TryParse(string text, out object value, out string error);

        /// <summary>
        /// The JSON value written to patches: a string, a <see cref="MasterDataJsonNumber"/> or another value
        /// <see cref="MasterDataJson"/> writes.
        /// </summary>
        object ToJson(object value);

        /// <summary>
        /// Reads a patch value: a string or a <see cref="MasterDataJsonNumber"/> (read from its <see cref="MasterDataJsonNumber.Raw"/>
        /// text, never through double). Throws <see cref="FormatException"/> for invalid values.
        /// </summary>
        object FromJson(object json);
    }

    /// <summary>
    /// Base of a converter of <typeparamref name="T"/>. Patches store the <see cref="Format(T)"/> text; a JSON number is read
    /// from its original text, so values keep every digit.
    /// </summary>
    public abstract class MasterDataValueConverter<T> : IMasterDataValueConverter
    {
        public Type ValueType => typeof(T);

        /// <inheritdoc cref="IMasterDataValueConverter.Format"/>
        public abstract string Format(T value);

        /// <inheritdoc cref="IMasterDataValueConverter.TryParse"/>
        public abstract bool TryParse(string text, out T value, out string error);

        /// <summary>Default: the <see cref="Format(T)"/> text as a JSON string.</summary>
        public virtual object ToJson(T value) => Format(value);

        /// <summary>Default: <see cref="TryParse(string, out T, out string)"/> of a JSON string or of a JSON number's text.</summary>
        public virtual T FromJson(object json)
        {
            string text;
            switch (json)
            {
                case string s:
                    text = s;
                    break;
                case MasterDataJsonNumber number:
                    text = number.Raw;
                    break;
                default:
                    throw new FormatException($"{typeof(T).Name} expects a JSON string or number.");
            }
            if (!TryParse(text, out var value, out var error)) throw new FormatException(error ?? $"'{text}' is not a valid {typeof(T).Name}.");
            return value;
        }

        string IMasterDataValueConverter.Format(object value) => Format((T)value);

        bool IMasterDataValueConverter.TryParse(string text, out object value, out string error)
        {
            if (TryParse(text ?? string.Empty, out var typed, out error))
            {
                value = typed;
                return true;
            }
            value = null;
            error ??= $"'{text}' is not a valid {typeof(T).Name}.";
            return false;
        }

        object IMasterDataValueConverter.ToJson(object value) => ToJson((T)value);

        object IMasterDataValueConverter.FromJson(object json) => FromJson(json);
    }

    /// <summary>
    /// Converters of custom value types (<see cref="IMasterDataValueConverter"/>). The game and the remote editor tool run the
    /// same registration: the tool needs it to edit the values, the game to read them from patches.
    /// <code>
    /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    /// static void RegisterConverters() =&gt; s_fix64 = MasterDataValueConverters.Register(new Fix64Converter());
    /// </code>
    /// </summary>
    public static class MasterDataValueConverters
    {
        static readonly object s_gate = new object();
        // replaced on every change, so lookups (every formatted value) need no lock
        static volatile Dictionary<Type, IMasterDataValueConverter> s_converters = new Dictionary<Type, IMasterDataValueConverter>();
        static int s_mainThreadId;

        /// <summary>
        /// Registers a converter. Dispose the returned token to remove it. Call on the main thread, before any table is
        /// registered: tables keep the member descriptors they were created with. Returns an empty token when the debugger
        /// is disabled in this build.
        /// </summary>
        /// <exception cref="ArgumentException">The type is a simple value, <see cref="Nullable{T}"/>, array, list, or not closed.</exception>
        /// <exception cref="InvalidOperationException">
        /// The type already has a converter, a table is already registered, or the call is not on the main thread.
        /// </exception>
        public static IDisposable Register(IMasterDataValueConverter converter)
        {
            if (converter == null) throw new ArgumentNullException(nameof(converter));
            var type = converter.ValueType ?? throw new ArgumentException("The converter has no ValueType.", nameof(converter));
            var problem = GetUnsupportedReason(type);
            if (problem != null) throw new ArgumentException($"{type.FullName} can not have a converter: {problem}.", nameof(converter));
            if (!MasterMemoryDebugBuild.IsEnabled) return EmptyToken.Instance;
            if (s_mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId != s_mainThreadId)
            {
                throw new InvalidOperationException("Value converters must be registered on the main thread.");
            }

            var token = new Token(type, converter);
            lock (s_gate)
            {
                if (s_converters.ContainsKey(type)) throw new InvalidOperationException("A converter is already registered for " + type.FullName);
                if (MasterMemoryDebugRegistry.Tables.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Register the converter of {type.FullName} before the tables: registered tables keep their member descriptors.");
                }
                s_converters = new Dictionary<Type, IMasterDataValueConverter>(s_converters) { { type, converter } };
                ClearCaches();
            }
            return token;
        }

        /// <summary>The converter registered for exactly <paramref name="type"/>.</summary>
        public static bool TryGet(Type type, out IMasterDataValueConverter converter)
        {
            converter = null;
            var converters = s_converters;
            return type != null && converters.Count != 0 && converters.TryGetValue(type, out converter);
        }

        static string GetUnsupportedReason(Type type)
        {
            if (type.ContainsGenericParameters) return "it is an open generic type";
            if (type.IsInterface || type.IsAbstract) return "values are never of an interface or abstract type";
            if (Nullable.GetUnderlyingType(type) != null) return "Nullable members use the converter of the underlying type";
            if (type.IsArray || (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))) return "lists use the converter of their element type";
            if (type.IsPrimitive || type.IsEnum || type.IsPointer || type.IsByRef || MasterDataValueUtility.GetBuiltInKind(type) != MasterDataValueKind.Complex)
            {
                return "the debugger edits it already";
            }
            return null;
        }

        static void ClearCaches()
        {
            MasterDataReflectionCache.Clear();
            MasterDataValueUtility.ClearEditableObjects();
        }

        internal static void ResetForTests() => ResetStatics();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lock (s_gate)
            {
                s_converters = new Dictionary<Type, IMasterDataValueConverter>();
                s_mainThreadId = Thread.CurrentThread.ManagedThreadId;
                ClearCaches();
            }
        }

        sealed class Token : IDisposable
        {
            readonly Type type;
            readonly IMasterDataValueConverter converter;
            bool disposed;

            public Token(Type type, IMasterDataValueConverter converter)
            {
                this.type = type;
                this.converter = converter;
            }

            public void Dispose()
            {
                lock (s_gate)
                {
                    if (disposed) return;
                    disposed = true;
                    if (!s_converters.TryGetValue(type, out var current) || current != converter) return;
                    var converters = new Dictionary<Type, IMasterDataValueConverter>(s_converters);
                    converters.Remove(type);
                    s_converters = converters;
                    ClearCaches();
                }
            }
        }

        sealed class EmptyToken : IDisposable
        {
            public static readonly EmptyToken Instance = new EmptyToken();
            public void Dispose() { }
        }
    }
}
