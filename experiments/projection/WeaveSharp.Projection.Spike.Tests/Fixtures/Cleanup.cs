class Lifetimes
{
    int Read(object gate)
    {
        try
        {
            using (var reader = Open())
            {
                lock (gate)
                {
                    if (Ready()) return reader.Read();
                    throw Problem();
                }
            }
        }
        catch (Problem error) when (Retry(error))
        {
            Write(error);
            return 5;
        }
        catch
        {
            throw;
        }
        finally
        {
            Close();
        }
    }

    void BreakWithCleanup()
    {
        while (Ready())
        {
            try
            {
                using (Open())
                {
                    if (Skip()) continue;
                    break;
                }
            }
            finally { Tick(); }
        }
    }
}
