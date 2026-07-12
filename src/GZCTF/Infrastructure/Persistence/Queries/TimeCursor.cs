using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace GZCTF.Infrastructure.Persistence.Queries;

public readonly record struct TimeCursor(DateTimeOffset Time, long Id)
{
    public string Encode() => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
        $"{Time.ToUniversalTime().UtcTicks.ToString(CultureInfo.InvariantCulture)}:{Id.ToString(CultureInfo.InvariantCulture)}"));

    public static TimeCursor Decode(string value)
    {
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(value.Trim());
            if (bytes.Length is < 3 or > 64)
                throw new FormatException();
            var parts = Encoding.UTF8.GetString(bytes).Split(':', StringSplitOptions.None);
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks || id <= 0)
                throw new FormatException();
            return new TimeCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidTimeCursorException(exception);
        }
    }
}

public readonly record struct GuidTimeCursor(DateTimeOffset Time, Guid Id)
{
    public string Encode() => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
        $"{Time.ToUniversalTime().UtcTicks.ToString(CultureInfo.InvariantCulture)}:{Id:N}"));

    public static GuidTimeCursor Decode(string value)
    {
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(value.Trim());
            if (bytes.Length is < 3 or > 96)
                throw new FormatException();
            var parts = Encoding.UTF8.GetString(bytes).Split(':', StringSplitOptions.None);
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !Guid.TryParseExact(parts[1], "N", out var id) || id == Guid.Empty ||
                ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
                throw new FormatException();
            return new GuidTimeCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidTimeCursorException(exception);
        }
    }
}

public sealed class InvalidTimeCursorException(Exception innerException) :
    Exception("The pagination cursor is invalid.", innerException);

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
