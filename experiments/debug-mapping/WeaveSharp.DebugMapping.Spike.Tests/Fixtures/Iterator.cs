using System.Collections.Generic;

static class YieldSample
{
    public static IEnumerable<int> Count(int limit)
    {
        for (int item = 0; item < limit; item++)
        {
            yield return item;
        }
        yield break;
    }
}
