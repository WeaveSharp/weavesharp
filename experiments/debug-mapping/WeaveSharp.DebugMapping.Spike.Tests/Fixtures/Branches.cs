static class ChoiceSample
{
    public static int Choose(int input)
    {
        int answer;
        if (input > 4)
        {
            answer = input - 2;
        }
        else
        {
            answer = input + 8;
        }
        return answer;
    }
}
