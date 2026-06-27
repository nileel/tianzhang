using System;
using System.Collections.Generic;
using System.Linq;

namespace BattleSim;

static class StageCombatReport
{
    public static IReadOnlyList<Character>[] SelectPools(IEnumerable<IReadOnlyList<Character>> sourcePools, string realm, int? subIndex = null)
    {
        if (sourcePools == null) throw new ArgumentNullException(nameof(sourcePools));
        if (string.IsNullOrWhiteSpace(realm)) throw new ArgumentException("Realm is required.", nameof(realm));

        return sourcePools
            .Select(pool => (IReadOnlyList<Character>)pool
                .Where(c => c.Realm == realm && (!subIndex.HasValue || c.SubIndex == subIndex.Value))
                .ToList())
            .ToArray();
    }

    public static void PrintSampleCounts(string title, string[] tags, IReadOnlyList<Character>[] pools, int totalSamples)
    {
        Console.WriteLine($"【{title}】");
        for (int i = 0; i < tags.Length; i++)
            Console.WriteLine($"  {tags[i],-8}: {pools[i].Count}/{totalSamples}");
    }
}
