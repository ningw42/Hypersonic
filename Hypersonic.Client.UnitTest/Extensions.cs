using System.Text;

namespace Hypersonic.Client.UnitTest;

public static class Extensions
{
    public static Stream ToStream(this string str)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(str));
    }
}
