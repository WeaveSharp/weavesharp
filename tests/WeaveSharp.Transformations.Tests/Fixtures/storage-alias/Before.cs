using System;
using System.Collections.Generic;

public static class Scenario
{
    private static readonly List<string> Effects = [];
    private static string mode = "";
    private static int[] cells = [10, 30];

    public static string[] Run(string scenario)
    {
        mode = scenario;
        if (mode == "overflow")
        {
            cells[0] = int.MaxValue;
        }

        try
        {
            checked
            {
                SelectCells()[SelectIndex()] += Delta();
            }
        }
        catch (Exception failure)
        {
            Effects.Add($"throw:{failure.GetType().Name}");
        }

        Effects.Add($"cells:{cells[0]}:{cells[1]}");
        return Effects.ToArray();
    }

    private static int[] SelectCells()
    {
        Effects.Add("array");
        return mode == "null-array" ? null! : cells;
    }

    private static int SelectIndex()
    {
        Effects.Add("index");
        return mode == "bad-index" ? 4 : 0;
    }

    private static int Delta()
    {
        Effects.Add("delta");
        if (mode == "delta-throws")
        {
            throw new ApplicationException();
        }

        if (mode == "mutating-delta")
        {
            cells[0] = 70;
            Effects.Add("delta-write:70");
        }

        return 3;
    }
}
