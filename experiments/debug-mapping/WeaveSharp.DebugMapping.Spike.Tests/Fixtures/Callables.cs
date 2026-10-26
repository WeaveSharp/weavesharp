using System;
using System.Threading.Tasks;

static class ClosureSample
{
    public static int Apply(int seed)
    {
        Func<int, int> multiply = item => item * 3;
        Func<int, int> shift = item =>
        {
            return item + 7;
        };
        int AddSeed(int item)
        {
            return item + seed;
        }
        return AddSeed(multiply(shift(seed)));
    }

    public static Task<int> Deferred(int seed)
    {
        async Task<int> Later()
        {
            await Task.Yield();
            return seed + 1;
        }
        return Later();
    }
}
