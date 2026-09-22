using System;
using System.Collections.Generic;
using System.Text;

namespace MFDExtension.Pages
{
    // One row of a compacted list: the first entry that carried its key, how
    // many were folded into it and the sum of their contributions.
    internal struct CollapsedEntry<T>
    {
        public T Sample;   // the first entry of the run: title, and whatever else the renderer needs
        public int Count;  // entries folded into this row; 1 renders exactly like an uncompacted one
        public double Sum; // summed contribution (kW, Ec/s...); 0 when the caller passes no value selector
    }

    // Folds entries sharing a key into one row, on the caller's side of the
    // list engine so ListGroup.Count stays the number of rows actually printed
    // (offsets and "+N MORE" need it). UnityEngine-free, like the engine.
    internal static class EntryCollapser
    {
        // One reusable index per element type: the collapse pass is
        // main-thread only and its dictionary never outlives the call.
        private static class Index<T>
        {
            public static readonly Dictionary<string, int> Map = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        // Fills `target` (cleared first) in first-appearance order, then sorts
        // it with `order` when given: summing changes a ranking by magnitude.
        // Entries with a null or empty key are never folded.
        public static void Collapse<T>(IList<T> source, List<CollapsedEntry<T>> target,
                                       Func<T, string> keyOf, Func<T, double> valueOf,
                                       Comparison<CollapsedEntry<T>> order)
        {
            target.Clear();
            if (source == null || source.Count == 0) return;

            Dictionary<string, int> index = Index<T>.Map;
            index.Clear();
            for (int i = 0; i < source.Count; ++i)
            {
                T item = source[i];
                string key = keyOf != null ? keyOf(item) : null;
                double value = valueOf != null ? valueOf(item) : 0.0;

                int at;
                if (!string.IsNullOrEmpty(key) && index.TryGetValue(key, out at))
                {
                    CollapsedEntry<T> merged = target[at];
                    merged.Count++;
                    merged.Sum += value;
                    target[at] = merged;
                    continue;
                }

                if (!string.IsNullOrEmpty(key)) index[key] = target.Count;
                target.Add(new CollapsedEntry<T> { Sample = item, Count = 1, Sum = value });
            }

            if (order != null) target.Sort(order);
        }

        // "(6) " in front of the title column, nothing for a single entry.
        // Width first, so the renderer can shrink the marquee budget before
        // writing anything.
        public static int CountPrefixWidth(int count)
        {
            if (count <= 1) return 0;
            int digits = 1;
            for (int value = count; value >= 10; value /= 10) digits++;
            return digits + 3; // "(", digits, ")", " "
        }

        public static void AppendCountPrefix(StringBuilder sb, int count)
        {
            if (count <= 1) return;
            sb.Append('(').Append(count).Append(") ");
        }
    }
}
