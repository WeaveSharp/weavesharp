class Ledger
{
    int Sum(int seed)
    {
        // Keep the spacing around this calculation.
        int first = seed + 3, second = Read(first);
        first += second;
        Write(first, Read(second));
        return first;
    }
}
