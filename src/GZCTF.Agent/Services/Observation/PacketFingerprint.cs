using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using PacketDotNet;

namespace GZCTF.Agent.Services.Observation;

public sealed record ParsedObservationPacket(
    string SourceIp,
    int? SourcePort,
    string DestinationIp,
    int? DestinationPort,
    string Protocol,
    byte? TcpFlags,
    int PacketLength,
    string PacketFingerprint,
    string FlowFingerprint);

public static class PacketFingerprint
{
    public static bool TryParse(LinkLayers linkLayer, ReadOnlySpan<byte> frame, out ParsedObservationPacket packet)
    {
        packet = default!;
        var offset = NetworkOffset(linkLayer, frame);
        if (offset < 0 || frame.Length < offset + 20) return false;
        var ip = frame[offset..];
        if ((ip[0] >> 4) != 4) return TryParseArp(linkLayer, frame, out packet);
        var headerLength = (ip[0] & 0x0f) * 4;
        if (headerLength < 20 || ip.Length < headerLength) return false;
        var declaredLength = BinaryPrimitives.ReadUInt16BigEndian(ip[2..4]);
        var capturedLength = Math.Min(declaredLength, ip.Length);
        if (capturedLength < headerLength) return false;
        var protocol = ip[9];
        var source = new IPAddress(ip[12..16]).ToString();
        var destination = new IPAddress(ip[16..20]).ToString();
        var transport = ip[headerLength..capturedLength];
        int? sourcePort = null;
        int? destinationPort = null;
        byte? flags = null;
        var payloadOffset = 0;
        var protocolName = protocol switch
        {
            6 => "TCP",
            17 => "UDP",
            1 => "ICMP",
            _ => $"IP-{protocol}"
        };
        using var canonical = new MemoryStream(256);
        canonical.Write(ip[4..8]);
        canonical.WriteByte(protocol);
        canonical.Write(ip[12..20]);
        switch (protocol)
        {
            case 6 when transport.Length >= 20:
            {
                sourcePort = BinaryPrimitives.ReadUInt16BigEndian(transport[..2]);
                destinationPort = BinaryPrimitives.ReadUInt16BigEndian(transport[2..4]);
                var tcpHeaderLength = (transport[12] >> 4) * 4;
                if (tcpHeaderLength < 20 || transport.Length < tcpHeaderLength) return false;
                flags = transport[13];
                canonical.Write(transport[..16]);
                canonical.Write(transport[18..tcpHeaderLength]);
                payloadOffset = tcpHeaderLength;
                break;
            }
            case 17 when transport.Length >= 8:
                sourcePort = BinaryPrimitives.ReadUInt16BigEndian(transport[..2]);
                destinationPort = BinaryPrimitives.ReadUInt16BigEndian(transport[2..4]);
                canonical.Write(transport[..6]);
                payloadOffset = 8;
                break;
            case 1 when transport.Length >= 8:
                canonical.Write(transport[..2]);
                canonical.Write(transport[4..8]);
                payloadOffset = 8;
                break;
            default:
                payloadOffset = 0;
                break;
        }
        if (transport.Length > payloadOffset)
            canonical.Write(transport[payloadOffset..]);
        packet = new ParsedObservationPacket(
            source,
            sourcePort,
            destination,
            destinationPort,
            protocolName,
            flags,
            declaredLength,
            Digest(canonical.ToArray()),
            FlowDigest(source, sourcePort, destination, destinationPort, protocolName));
        return true;
    }

    private static bool TryParseArp(
        LinkLayers linkLayer,
        ReadOnlySpan<byte> frame,
        out ParsedObservationPacket packet)
    {
        packet = default!;
        var offset = NetworkOffset(linkLayer, frame);
        if (offset < 0 || frame.Length < offset + 28) return false;
        if (linkLayer == LinkLayers.Ethernet && BinaryPrimitives.ReadUInt16BigEndian(frame[12..14]) != 0x0806)
            return false;
        var arp = frame[offset..];
        if (BinaryPrimitives.ReadUInt16BigEndian(arp[..2]) != 1 ||
            BinaryPrimitives.ReadUInt16BigEndian(arp[2..4]) != 0x0800 || arp[4] != 6 || arp[5] != 4)
            return false;
        var source = new IPAddress(arp[14..18]).ToString();
        var destination = new IPAddress(arp[24..28]).ToString();
        packet = new ParsedObservationPacket(
            source,
            null,
            destination,
            null,
            "ARP",
            null,
            arp.Length,
            Digest(arp[..28].ToArray()),
            FlowDigest(source, null, destination, null, "ARP"));
        return true;
    }

    private static int NetworkOffset(LinkLayers linkLayer, ReadOnlySpan<byte> frame)
    {
        if (linkLayer == LinkLayers.Ethernet)
        {
            if (frame.Length < 14) return -1;
            var offset = 14;
            var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame[12..14]);
            while (etherType is 0x8100 or 0x88a8)
            {
                if (frame.Length < offset + 4) return -1;
                etherType = BinaryPrimitives.ReadUInt16BigEndian(frame[(offset + 2)..(offset + 4)]);
                offset += 4;
            }
            return etherType is 0x0800 or 0x0806 ? offset : -1;
        }
        return linkLayer == LinkLayers.LinuxSll ? 16 : -1;
    }

    private static string FlowDigest(
        string source,
        int? sourcePort,
        string destination,
        int? destinationPort,
        string protocol) => Digest(Encoding.UTF8.GetBytes(
        $"{source}|{sourcePort?.ToString() ?? ""}|{destination}|{destinationPort?.ToString() ?? ""}|{protocol}"));

    private static string Digest(byte[] value) =>
        $"sha256:{Convert.ToHexStringLower(SHA256.HashData(value))}";
}
