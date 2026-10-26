static class IterationSample
{
    public static int Accumulate(int limit, int[] items)
    {
        int total = 0;
        for (int step = 0; step < limit; step++)
        {
            if (step == 2) continue;
            if (step == 9) break;
            total += step;
        }
        while (total < limit)
        {
            total++;
        }
        do
        {
            total--;
        } while (total > limit);
        foreach (int item in items)
        {
            total += item;
        }
        return total;
    }
}
