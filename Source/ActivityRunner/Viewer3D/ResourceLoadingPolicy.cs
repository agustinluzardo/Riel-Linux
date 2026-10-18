using System;
using System.Collections.Generic;

namespace Orts.ActivityRunner.Viewer3D
{
    internal static class TexturePreloadBudget
    {
        internal static long Limit(long availableBytes)
        {
            if (availableBytes <= 0)
                return 0;
            // Optional preloading must leave useful headroom, including on small
            // memory-limited systems. This limit applies to deferred bulk loading.
            long reserve = Math.Min(512L * 1024 * 1024, availableBytes / 8);
            return availableBytes - reserve;
        }
    }

    // Built once per mark pass, rather than searching the entire cache for each
    // live resource. Every alias must be marked to avoid disposing a live object.
    internal sealed class ResourceMarkIndex<TKey, TResource> where TResource : class
    {
        private readonly Dictionary<TResource, List<TKey>> keys = new(ReferenceEqualityComparer.Instance);

        internal void Clear() => keys.Clear();

        internal void Reset(IEnumerable<KeyValuePair<TKey, TResource>> resources)
        {
            keys.Clear();
            foreach (var entry in resources)
            {
                if (entry.Value == null)
                    continue;
                if (!keys.TryGetValue(entry.Value, out List<TKey> aliases))
                    keys.Add(entry.Value, aliases = new List<TKey>());
                aliases.Add(entry.Key);
            }
        }

        internal void Mark(TResource resource, IDictionary<TKey, bool> marks)
        {
            if (resource != null && keys.TryGetValue(resource, out List<TKey> aliases))
                foreach (TKey key in aliases)
                    marks[key] = true;
        }
    }

    internal sealed class ResourceSweepSchedule
    {
        private object lastResources;
        private TimeSpan lastSweep;

        internal bool ShouldSweep(bool tileChanged, object resources, TimeSpan now)
        {
            if (!tileChanged && ReferenceEquals(resources, lastResources))
                return false;
            // Coalesce AI churn while parked. Tile changes retain immediate cleanup.
            if (!tileChanged && lastResources != null && now - lastSweep < TimeSpan.FromSeconds(30))
                return false;
            lastResources = resources;
            lastSweep = now;
            return true;
        }
    }
}
