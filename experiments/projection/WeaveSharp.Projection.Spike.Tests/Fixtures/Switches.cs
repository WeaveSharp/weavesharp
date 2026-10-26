class Labels
{
    int Select(int key)
    {
        switch (key)
        {
            case 1:
            case 2:
                Write(key);
                break;
            case 7:
                return 11;
            default:
                key++;
                break;
        }
        return key;
    }

    int Expression(int key) => key switch { 0 => 6, _ => 8 };

    int Pattern(object item)
    {
        if (item is int number and > 0) return number;
        switch (item)
        {
            case string { Length: > 2 } text: return text.Length;
            default: return 0;
        }
    }
}
