class Counters
{
    void Count(int n, int[] items)
    {
        while (n > 0)
        {
            n--;
            if (n == 3) continue;
            if (n == 1) break;
            Write(n);
        }
        do { n++; } while (n < 2);
        for (int i = Start(); i < 5; i++, Tick())
        {
            if (i == 2) continue;
            Write(i);
        }
        foreach (var item in items)
        {
            if (item == 0) continue;
            if (item < 0) break;
            Write(item);
        }
        for (;;) { if (Done()) break; }
    }
}
