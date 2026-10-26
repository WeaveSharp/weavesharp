class Routes
{
    int Choose(int score)
    {
        if (score > 4)
        {
            Write(score);
        }
        else if (score == 0)
            return 9;
        else
            score++;
        if (score < 0) return 2;
        else return score;
        Write(100); // Still belongs to the source even when unreachable.
    }
}
