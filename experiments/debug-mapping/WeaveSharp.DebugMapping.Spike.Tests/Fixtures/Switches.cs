static class DispatchSample
{
    public static int Select(int code)
    {
        int result = 0;
        switch (code)
        {
            case 1:
            case 3:
                result = 10;
                break;
            case 8:
                result = 20;
                break;
            default:
                result = 30;
                break;
        }
        return result;
    }

    public static int Expression(int code)
    {
        return code switch
        {
            1 => 40,
            3 => 50,
            _ => 60
        };
    }
}
