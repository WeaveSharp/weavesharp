class Callables
{
    int saved;
    Callables(int seed) { saved = seed; }
    Callables() => saved = 4;
    int Value { get { return saved; } set => saved = value; }
    int Twice => saved * 2;
    int this[int offset] => saved + offset;

    int Outer()
    {
        int Local(int input) { return input + saved; }
        int Other(int input) => input - 1;
        Func<int, int> plus = input => input + 1;
        Func<int, int> minus = (input) => { return input - 1; };
        Action idle = delegate { Tick(); };
        return Local(Other(saved));
    }
}
