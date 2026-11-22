using System.Net;
using System.Net.Sockets;

namespace Loans.Web.OAuth;

/// <summary>SSRF guard: only globally routable unicast addresses may be fetched.</summary>
public static class IpAddressClassifier
{
    public static bool IsPubliclyRoutable(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicIPv4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicIPv6(address.GetAddressBytes()),
            _ => false,
        };
    }

    private static bool IsPublicIPv4(byte[] b) => !(
        b[0] == 0                                          // "this" network / unspecified
        || b[0] == 10                                      // RFC 1918
        || (b[0] == 100 && b[1] is >= 64 and <= 127)       // CGNAT
        || b[0] == 127                                     // loopback
        || (b[0] == 169 && b[1] == 254)                    // link-local (incl. cloud metadata)
        || (b[0] == 172 && b[1] is >= 16 and <= 31)        // RFC 1918
        || (b[0] == 192 && b[1] == 0 && b[2] == 0)         // IETF protocol assignments
        || (b[0] == 192 && b[1] == 0 && b[2] == 2)         // documentation
        || (b[0] == 192 && b[1] == 168)                    // RFC 1918
        || (b[0] == 198 && b[1] is 18 or 19)               // benchmarking
        || (b[0] == 198 && b[1] == 51 && b[2] == 100)      // documentation
        || (b[0] == 203 && b[1] == 0 && b[2] == 113)       // documentation
        || b[0] >= 224);                                   // multicast, reserved, broadcast

    private static bool IsPublicIPv6(byte[] b)
    {
        // Only global unicast (2000::/3). Excludes loopback, unspecified, link-local, ULA, multicast,
        // IPv4-compatible and NAT64 (64:ff9b::/96).
        if ((b[0] & 0xE0) != 0x20)
        {
            return false;
        }

        var isTeredo = b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00;
        var isDocumentation = b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8;
        var is6To4 = b[0] == 0x20 && b[1] == 0x02; // can embed a private IPv4 address

        return !isTeredo && !isDocumentation && !is6To4;
    }
}
