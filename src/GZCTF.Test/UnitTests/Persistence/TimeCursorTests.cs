using System;
using GZCTF.Infrastructure.Persistence.Queries;
using Xunit;

namespace GZCTF.Test.UnitTests.Persistence;

public sealed class TimeCursorTests
{
    [Fact]
    public void TimeCursor_RoundTripsUtcTimeAndId()
    {
        var cursor = new TimeCursor(DateTimeOffset.Parse("2026-07-12T12:34:56.1234567+08:00"), 42);

        var decoded = TimeCursor.Decode(cursor.Encode());

        Assert.Equal(cursor.Time.ToUniversalTime(), decoded.Time);
        Assert.Equal(cursor.Id, decoded.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64")]
    [InlineData("MTow")]
    public void TimeCursor_RejectsMalformedOrNonPositiveValues(string value) =>
        Assert.Throws<InvalidTimeCursorException>(() => TimeCursor.Decode(value));

    [Fact]
    public void GuidTimeCursor_RoundTripsStableGuid()
    {
        var cursor = new GuidTimeCursor(DateTimeOffset.Parse("2026-07-12T04:00:00Z"), Guid.CreateVersion7());

        Assert.Equal(cursor, GuidTimeCursor.Decode(cursor.Encode()));
    }
}
