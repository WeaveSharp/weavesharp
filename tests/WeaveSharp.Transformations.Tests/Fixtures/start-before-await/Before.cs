using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class Scenario
{
    private static readonly List<string> Effects = [];
    private static string mode = "";

    public static async Task<string[]> Run(string scenario)
    {
        mode = scenario;
        try
        {
            string[] result = await Gather().ConfigureAwait(false);
            Effects.Add($"result:{result[0]}:{result[1]}");
        }
        catch (Exception failure)
        {
            Effects.Add($"throw:{failure.GetType().Name}");
        }

        return Effects.ToArray();
    }

    private static async Task<string[]> Gather()
    {
        string oak = await Start("oak").ConfigureAwait(false);
        Effects.Add($"observed:{oak}");
        string elm = await Start("elm").ConfigureAwait(false);
        Effects.Add($"observed:{elm}");
        return [oak, elm];
    }

    private static Task<string> Start(string item)
    {
        Effects.Add($"start:{item}");
        if (mode == "synchronous-failure" && item == "oak")
        {
            throw new InvalidOperationException();
        }

        if ((mode == "first-fault" && item == "oak") ||
            (mode == "second-fault" && item == "elm"))
        {
            return Task.FromException<string>(new ApplicationException());
        }

        return Task.FromResult(item == "oak" ? "O" : "E");
    }
}
