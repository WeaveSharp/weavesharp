using System.Threading.Tasks;

static class SuspensionSample
{
    public static async Task<int> Collect(int seed)
    {
        int amount = await Task.FromResult(seed);
        amount += await Task.FromResult(5);
        await Task.Yield();
        return amount;
    }
}
