using System;
using System.Collections.Generic;

public static class Scenario
{
    private static readonly List<string> Effects = [];
    private static string mode = "";
    private static int stored = 17;

    public static string[] Run(string scenario)
    {
        mode = scenario;
        try
        {
            int result = ReadAndClose();
            Effects.Add($"caller:{result}");
        }
        catch (Exception failure)
        {
            Effects.Add($"throw:{failure.GetType().Name}");
        }

        Effects.Add($"stored:{stored}");
        return Effects.ToArray();
    }

    private static int ReadAndClose()
    {
        try
        {
            int pending = Read();
            return pending;
        }
        finally
        {
            Close();
        }
    }

    private static int Read()
    {
        Effects.Add("read");
        if (mode is "read-throws" or "both-throw")
        {
            throw new InvalidOperationException();
        }

        return stored;
    }

    private static void Close()
    {
        Effects.Add("cleanup");
        stored = 91;
        if (mode is "cleanup-throws" or "both-throw")
        {
            throw new ApplicationException();
        }
    }
}
