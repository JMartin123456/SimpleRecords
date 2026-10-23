using System.Net;
using System.Net.Sockets;

namespace SimpleRecords.Server.Services;

public static class IpAddressValidator
{
    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var ip = value.Trim();

        if (!IPAddress.TryParse(ip, out var parsedIp))
        {
            return false;
        }

        if (parsedIp.AddressFamily == AddressFamily.InterNetwork)
        {
            var parts = ip.Split('.');

            return parts.Length == 4 &&
                   parts.All(part =>
                       part.Length is >= 1 and <= 3 &&
                       part.All(char.IsAsciiDigit) &&
                       int.TryParse(part, out int number) &&
                       number <= 255);
        }

        return parsedIp.AddressFamily == AddressFamily.InterNetworkV6
               && ip.Contains(':');
    }
}
