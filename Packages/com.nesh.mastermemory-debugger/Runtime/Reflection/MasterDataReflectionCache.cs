using System;
using System.Collections.Generic;
using System.Reflection;
using MasterMemory;
using UnityEngine;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>
    /// Scans record types once and caches the result.
    /// Only plain reflection (no Reflection.Emit / DynamicMethod) is used so it works on WebGL and IL2CPP.
    /// </summary>
    public static class MasterDataReflectionCache
    {
        static readonly Dictionary<Type, MasterDataTypeDescriptor> s_cache = new Dictionary<Type, MasterDataTypeDescriptor>();
        static readonly object s_gate = new object();

        public static MasterDataTypeDescriptor Get(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            lock (s_gate)
            {
                if (!s_cache.TryGetValue(type, out var descriptor))
                {
                    descriptor = Create(type);
                    s_cache.Add(type, descriptor);
                }
                return descriptor;
            }
        }

        public static MasterDataTypeDescriptor Get<T>() => Get(typeof(T));

        internal static void Clear()
        {
            lock (s_gate) s_cache.Clear();
        }

        static MasterDataTypeDescriptor Create(Type type)
        {
            var primaryKeys = new List<MasterMemoryFieldDescriptor>();
            var others = new List<MasterMemoryFieldDescriptor>();
            var order = 0;

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                if (property.GetGetMethod() == null) continue;

                var primaryKey = property.GetCustomAttribute<PrimaryKeyAttribute>(true);
                var isSecondaryKey = property.IsDefined(typeof(SecondaryKeyAttribute), true);
                var setter = CreatePropertySetter(type, property);
                var p = property;
                Func<object, object> getter = record => p.GetValue(record);
                if (setter == null && primaryKey == null && !isSecondaryKey)
                {
                    // computed property: never let a throwing getter break the inspector or patch creation
                    getter = record => GetComputedValue(p, record);
                }
                var field = new MasterMemoryFieldDescriptor(
                    property,
                    property.PropertyType,
                    getter,
                    setter,
                    primaryKey != null,
                    primaryKey?.KeyOrder ?? 0,
                    isSecondaryKey,
                    order++);
                (field.IsPrimaryKey ? primaryKeys : others).Add(field);
            }

            foreach (var fieldInfo in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                var f = fieldInfo;
                var field = new MasterMemoryFieldDescriptor(
                    fieldInfo,
                    fieldInfo.FieldType,
                    record => f.GetValue(record),
                    (record, value) => f.SetValue(record, value),
                    false,
                    0,
                    false,
                    order++);
                others.Add(field);
            }

            primaryKeys.Sort((a, b) => a.PrimaryKeyOrder.CompareTo(b.PrimaryKeyOrder));
            primaryKeys.AddRange(others);
            return new MasterDataTypeDescriptor(type, primaryKeys);
        }

        static object GetComputedValue(PropertyInfo property, object record)
        {
            try
            {
                return property.GetValue(record);
            }
            catch (TargetInvocationException e)
            {
                return "<error: " + (e.InnerException ?? e).Message + ">";
            }
        }

        static Action<object, object> CreatePropertySetter(Type type, PropertyInfo property)
        {
            // set / init / private set
            if (property.GetSetMethod(true) != null)
            {
                return (record, value) => property.SetValue(record, value);
            }

            // get-only auto property: write the compiler generated backing field
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var backingField = t.GetField("<" + property.Name + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (backingField != null)
                {
                    return (record, value) => backingField.SetValue(record, value);
                }
            }

            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();
    }
}
