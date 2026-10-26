class OpaqueSamples
{
    void Run(object item)
    {
        var numbers = new[] { 3, 5 };
        var selected = from number in numbers where number > 3 select number;
        using var lease = Open();
        Write(item?.ToString());
        if (Ready() && Next()) Write(1);
        checked { Count(); }
        goto finish;
        finish: Write(2);
    }

    IEnumerable<int> Stream()
    {
        yield return 4;
        yield break;
    }

    unsafe void Raw(int* p)
    {
        fixed (int* address = &p[0]) { *address = 1; }
    }
}
