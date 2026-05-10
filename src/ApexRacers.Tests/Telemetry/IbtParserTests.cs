using ApexRacers.Api.Telemetry;
using ApexRacers.Tests.Helpers;
using Xunit;

namespace ApexRacers.Tests.Telemetry;

public class IbtParserTests
{
    [Fact]
    public void Parse_NonSeekableStream_ThrowsArgumentException()
    {
        using var nonSeekable = new NonSeekableStream(new MemoryStream(new byte[256]));
        Assert.Throws<ArgumentException>(() => IbtParser.Parse(nonSeekable));
    }

    [Fact]
    public void Parse_UnsupportedVersion_ThrowsInvalidDataException()
    {
        // Build a stream with version = 5 (outside accepted range 1-2)
        var buf = new byte[144];
        BitConverter.GetBytes(5).CopyTo(buf, 0); // version
        using var stream = new MemoryStream(buf);
        Assert.Throws<InvalidDataException>(() => IbtParser.Parse(stream));
    }

    [Fact]
    public void Parse_ValidStream_ReturnsCorrectSessionInfo()
    {
        using var stream = FakeIbtBuilder.Build(
            laps: 1,
            trackId: 42,
            trackName: "Spa-Francorchamps",
            configName: "Full",
            carId: 99,
            carName: "Porsche 992 GT3",
            carNameShort: "P992",
            customerId: 12345,
            driverName: "Jerry Holland");

        var session = IbtParser.Parse(stream);

        Assert.Equal(42, session.IracingTrackId);
        Assert.Equal("Spa-Francorchamps", session.TrackName);
        Assert.Equal("Full", session.ConfigName);
        Assert.Equal(99, session.IracingCarId);
        Assert.Equal("Porsche 992 GT3", session.CarName);
        Assert.Equal("P992", session.CarNameAbbreviated);
        Assert.Equal(12345L, session.DriverCustomerId);
        Assert.Equal("Jerry Holland", session.DriverName);
        Assert.Equal(25.0f, session.AirTempCelsius, precision: 1);
        Assert.Equal(35.0f, session.TrackTempCelsius, precision: 1);
    }

    [Fact]
    public void Parse_ValidLaps_ReturnsCorrectLapCountAndMarksAsValid()
    {
        using var stream = FakeIbtBuilder.Build(laps: 3, lapTime: 90.5f, validLaps: true);

        var session = IbtParser.Parse(stream);

        Assert.Equal(3, session.Laps.Count);
        Assert.All(session.Laps, l => Assert.True(l.IsValid));
        Assert.All(session.Laps, l => Assert.Equal(90.5, l.LapTimeSeconds, precision: 2));
    }

    [Fact]
    public void Parse_NegativeLapTimes_MarksLapsInvalid()
    {
        using var stream = FakeIbtBuilder.Build(laps: 2, validLaps: false);

        var session = IbtParser.Parse(stream);

        Assert.Equal(2, session.Laps.Count);
        Assert.All(session.Laps, l => Assert.False(l.IsValid));
    }

    [Fact]
    public void Parse_NoLapVarsInFile_ReturnsEmptyLapsList()
    {
        using var stream = FakeIbtBuilder.Build(laps: 2, includesLapVars: false);

        var session = IbtParser.Parse(stream);

        Assert.Empty(session.Laps);
    }

    [Fact]
    public void Parse_EmptyCarNameShort_FallsBackToCarName()
    {
        using var stream = FakeIbtBuilder.Build(carName: "Ferrari 296 GT3", carNameShort: "");

        var session = IbtParser.Parse(stream);

        Assert.Equal("Ferrari 296 GT3", session.CarNameAbbreviated);
    }

    [Fact]
    public void Parse_LapNumbers_AreSequential()
    {
        using var stream = FakeIbtBuilder.Build(laps: 3);

        var session = IbtParser.Parse(stream);

        Assert.Equal([1, 2, 3], session.Laps.Select(l => l.LapNumber).ToArray());
    }

    [Fact]
    public void Parse_SessionDate_IsDecodedFromUnixTimestamp()
    {
        // Unix timestamp 1_000_000 = 2001-09-08 21:46:40 UTC
        using var stream = FakeIbtBuilder.Build(sessionDate: 1_000_000.0);

        var session = IbtParser.Parse(stream);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_000_000), session.SessionDate);
    }

    // ── Helper: wraps a stream and disables seeking ───────────────────────────

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead  => inner.CanRead;
        public override bool CanSeek  => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length   => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void  Flush()                                => inner.Flush();
        public override int   Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long  Seek(long offset, SeekOrigin origin)   => throw new NotSupportedException();
        public override void  SetLength(long value)                  => throw new NotSupportedException();
        public override void  Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); }
    }
}
