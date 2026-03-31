using System.Net;
using System.Linq;

namespace MotorcycleRAG.Admin.Utilities;

/// <summary>
/// Utility for URL validation with SSRF protection.
/// Blocks loopback addresses, private IP ranges, and reserved addresses.
/// </summary>
internal static class UrlValidator
{
    private static readonly int[] StandardPorts = { 80, 443 };
    private static readonly int[] DevelopmentPorts = { 8000, 8001, 8002, 8003, 8004, 8005, 8006, 8007, 8008, 8009 };

    /// <summary>
    /// Validates a URL for SSRF protection.
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <param name="allowLocalhost">Whether to allow localhost/loopback addresses (e.g. for local testing)</param>
    /// <returns>True if the URL is valid and safe, false otherwise</returns>
    internal static bool IsValidUrl(string url, bool allowLocalhost = false)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return IsValidUrl(uri, allowLocalhost);
    }

    /// <summary>
    /// Validates a URL for SSRF protection.
    /// </summary>
    /// <param name="uri">The URI to validate</param>
    /// <param name="allowLocalhost">Whether to allow localhost/loopback addresses (e.g. for local testing)</param>
    /// <returns>True if the URL is valid and safe, false otherwise</returns>
    internal static bool IsValidUrl(Uri uri, bool allowLocalhost = false)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // Only allow HTTP/HTTPS schemes
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        var isLocalhostAddress = uri.IsLoopback || IsLocalhostAddress(uri.Host);

        // Allow localhost endpoints explicitly for development workflows.
        if (isLocalhostAddress)
            return allowLocalhost;

        // Block private IP ranges
        if (IsPrivateIpAddress(uri.Host))
            return false;

        // Block reserved and special addresses
        if (IsReservedAddress(uri.Host))
            return false;

        // Port validation
        if (uri.Port > 0)
        {
            if (DevelopmentPorts.Contains(uri.Port) && !isLocalhostAddress)
            {
                // Development ports allowed only on localhost
                return false;
            }
            else if (!isLocalhostAddress && !StandardPorts.Contains(uri.Port))
            {
                // Port not in allowed lists
                return false;
            }
        }

        return true;
    }

    private static bool IsLocalhostAddress(string host)
    {
        var normalizedHost = host.Trim('[', ']');
        return normalizedHost == "localhost" || normalizedHost == "127.0.0.1" || normalizedHost == "::1";
    }

    private static bool IsLoopbackAddress(string host)
    {
        return IsLocalhostAddress(host);
    }

    private static bool IsPrivateIpAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var ipAddress))
            return false;

        var octets = ipAddress.ToString().Split('.');

        // 10.x.x.x
        if (octets.Length == 4 && octets[0] == "10")
            return true;

        // 172.16.x.x to 172.31.x.x
        if (octets.Length == 4 && octets[0] == "172" && int.TryParse(octets[1], out var secondOctet) && secondOctet >= 16 && secondOctet <= 31)
        {
            return true;
        }

        // 192.168.x.x
        if (octets.Length == 4 && octets[0] == "192" && octets[1] == "168")
            return true;

        return false;
    }

    private static bool IsReservedAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var ipAddress))
            return false;

        var octets = ipAddress.ToString().Split('.');

        // 127.x.x.x
        if (octets.Length == 4 && octets[0] == "127")
            return true;

        // 169.254.x.x
        if (octets.Length == 4 && octets[0] == "169" && octets[1] == "254")
            return true;

        // 224.x.x.x to 239.x.x.x
        if (octets.Length == 4 && int.TryParse(octets[0], out var firstOctet) && firstOctet >= 224 && firstOctet <= 239)
        {
            return true;
        }

        // 255.255.255.255 or 0.0.0.0
        if (host == "255.255.255.255" || host == "0.0.0.0")
            return true;

        return false;
    }
}
