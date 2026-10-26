class Waiting
{
    async Task<int> Fetch()
    {
        var pending = Request();
        int value = await pending;
        await WriteAsync(value);
        await using (var lease = Acquire())
        {
            return await ReadAsync(lease);
        }
    }

    async Task<int> Expression() => await Request();
}
