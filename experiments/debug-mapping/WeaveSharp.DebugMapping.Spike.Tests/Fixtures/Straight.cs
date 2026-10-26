using System;

static class ArithmeticSample
{
    public static int Sum(int left, int right)
    {
        int total = left;
        total = Math.Abs(left) + Math.Abs(right) * 2;
        total += 3;
        return total;
    }

    public static int Twice(int input) => input * 2;
}
