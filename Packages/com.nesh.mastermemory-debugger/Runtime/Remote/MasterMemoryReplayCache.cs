using System;
using System.Collections.Generic;

namespace Nesh.MasterMemoryDebugger
{
    /// <summary>Bounded replay cache for remote request ids. Oldest entries are evicted automatically.</summary>
    internal sealed class MasterMemoryReplayCache<T>
    {
        readonly int capacity;
        readonly Dictionary<string, T> values = new Dictionary<string, T>(StringComparer.Ordinal);
        readonly Queue<string> order = new Queue<string>();

        public MasterMemoryReplayCache(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
        }

        public int Count => values.Count;

        public bool TryGetValue(string key, out T value) => values.TryGetValue(key, out value);

        public void Add(string key, T value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Replay key is required.", nameof(key));
            if (values.ContainsKey(key))
            {
                values[key] = value;
                return;
            }
            while (values.Count >= capacity && order.Count > 0)
            {
                var oldest = order.Dequeue();
                values.Remove(oldest);
            }
            values.Add(key, value);
            order.Enqueue(key);
        }

        public void Clear()
        {
            values.Clear();
            order.Clear();
        }
    }
}
