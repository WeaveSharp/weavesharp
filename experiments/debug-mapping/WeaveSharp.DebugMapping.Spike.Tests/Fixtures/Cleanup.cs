using System;

static class ResourceSample
{
    public static int Parse(string text, IDisposable lease, object gate)
    {
        int result = 0;
        try
        {
            result = int.Parse(text);
        }
        catch (FormatException failure) when (failure.Message.Length > 2)
        {
            result = -4;
        }
        finally
        {
            result += 2;
        }
        using (lease)
        {
            result++;
        }
        lock (gate)
        {
            result += 3;
        }
        return result;
    }
}
