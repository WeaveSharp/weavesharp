using System;
using System.Collections.Generic;

public static class Scenario
{
    private static readonly List<string> Effects = [];
    private static string mode = "";

    public static string[] Run(string scenario)
    {
        mode = scenario;
        try
        {
            bool result = ShouldPack(ReadLeft, ReadRight);
            Effects.Add(result ? "result:true" : "result:false");
        }
        catch (Exception failure)
        {
            Effects.Add($"throw:{failure.GetType().Name}");
        }

        return Effects.ToArray();
    }

    private static bool ShouldPack(Func<bool> available, Func<bool> fits)
    {
        bool inStock = available();
        bool suitable = fits();
        return inStock && suitable;
    }

    private static bool ReadLeft()
    {
        Effects.Add("left");
        if (mode == "left-throws")
        {
            throw new InvalidOperationException();
        }

        return mode is not ("left-false" or "skipped-throw");
    }

    private static bool ReadRight()
    {
        Effects.Add("right");
        if (mode is "right-throws" or "skipped-throw")
        {
            throw new ApplicationException();
        }

        return mode != "right-false";
    }
}
