using System;
using System.Collections.Generic;

public static class Scenario
{
    private static readonly List<string> Effects = [];
    private static string mode = "";
    private static int ticket;

    public static string[] Run(string scenario)
    {
        mode = scenario;
        try
        {
            ParcelDesk desk = OpenDesk();
            string label = MakeLabel();
            int number = NextTicket();
            desk.Accept(label, number);
        }
        catch (Exception failure)
        {
            Effects.Add($"throw:{failure.GetType().Name}");
        }

        Effects.Add($"ticket:{ticket}");
        return Effects.ToArray();
    }

    private static ParcelDesk OpenDesk()
    {
        Effects.Add("receiver");
        if (mode == "receiver-throws")
        {
            throw new InvalidOperationException();
        }

        return mode == "null-receiver" ? null! : new ParcelDesk();
    }

    private static string MakeLabel()
    {
        Effects.Add("label");
        if (mode == "argument-throws")
        {
            throw new ApplicationException();
        }

        return "parcel";
    }

    private static int NextTicket()
    {
        Effects.Add("ticket-next");
        return ++ticket;
    }

    private sealed class ParcelDesk
    {
        public void Accept(string label, int number) => Effects.Add($"accept:{label}:{number}");

        public void Accept(object label, int number) => Effects.Add("unexpected-overload");
    }
}
