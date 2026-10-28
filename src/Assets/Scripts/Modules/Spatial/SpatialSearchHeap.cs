using System;
using System.Collections.Generic;

namespace TianZhang.Spatial
{
    /// <summary>Stable binary min-heap. Equal priorities retain the old frontier's insertion order.</summary>
    internal sealed class SpatialSearchHeap<T> where T : IComparable<T>
    {
        private readonly List<(T value, long sequence)> entries = new();
        private long sequence;
        public int Count => entries.Count;

        public void Enqueue(T value)
        {
            var entry = (value, sequence++);
            int index = entries.Count;
            entries.Add(entry);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (Compare(entries[parent], entry) <= 0) break;
                entries[index] = entries[parent];
                index = parent;
            }
            entries[index] = entry;
        }

        public T Dequeue()
        {
            T result = entries[0].value;
            var last = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            if (entries.Count == 0) return result;
            int index = 0;
            while (index * 2 + 1 < entries.Count)
            {
                int child = index * 2 + 1;
                if (child + 1 < entries.Count && Compare(entries[child + 1], entries[child]) < 0) child++;
                if (Compare(last, entries[child]) <= 0) break;
                entries[index] = entries[child];
                index = child;
            }
            entries[index] = last;
            return result;
        }

        private static int Compare((T value, long sequence) a, (T value, long sequence) b)
        {
            int priority = a.value.CompareTo(b.value);
            return priority != 0 ? priority : a.sequence.CompareTo(b.sequence);
        }
    }
}
