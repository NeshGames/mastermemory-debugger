using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Creates editable copies of records so that instances owned by the MasterMemory database are never modified.
    /// Order: registered clone provider, then a shallow member-wise clone.
    /// The shallow clone is sufficient because only simple value members can be edited;
    /// complex members (arrays, lists, nested objects) are read-only and may be shared with the original.
    /// </summary>
    public static class MasterDataCloneUtility
    {
        static readonly MethodInfo s_memberwiseClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        static readonly Dictionary<Type, Func<object, object>> s_providers = new Dictionary<Type, Func<object, object>>();
        static readonly object s_gate = new object();

        public static T Clone<T>(T source)
        {
            return (T)Clone((object)source);
        }

        public static object Clone(object source)
        {
            if (source == null) return null;

            Func<object, object> provider;
            lock (s_gate) s_providers.TryGetValue(source.GetType(), out provider);

            var clone = provider != null ? provider(source) : s_memberwiseClone.Invoke(source, null);
            if (ReferenceEquals(clone, source) && !source.GetType().IsValueType)
            {
                throw new InvalidOperationException(
                    $"Clone provider for {source.GetType().FullName} returned the source instance. A new instance is required.");
            }
            return clone;
        }

        internal static void RegisterProvider(Type type, Func<object, object> provider)
        {
            lock (s_gate)
            {
                if (provider == null) s_providers.Remove(type);
                else s_providers[type] = provider;
            }
        }

        internal static void ClearProviders()
        {
            lock (s_gate) s_providers.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ClearProviders();
    }
}
