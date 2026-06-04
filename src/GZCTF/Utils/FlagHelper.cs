namespace GZCTF.Utils;

public static class FlagHelper
{
    private static readonly Random Random = new();
    private const string Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string GenerateFlag(int length)
    {
        var flag = new char[length];
        for (int i = 0; i < length; i++)
            flag[i] = Chars[Random.Next(Chars.Length)];
        return $"flag{{{new string(flag)}}}";
    }
}
