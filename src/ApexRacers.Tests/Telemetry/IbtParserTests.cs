using ApexRacers.Api.Telemetry;
using ApexRacers.Core.Models;
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
    public void Parse_FileTooSmall_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[100]);
        Assert.Throws<InvalidDataException>(() => IbtParser.Parse(stream));
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
    public void Parse_InvalidSessionInfoOffset_ThrowsInvalidDataException()
    {
        // sessionInfoOffset = 0 is below the 144-byte header floor
        var buf = new byte[144];
        BitConverter.GetBytes(1).CopyTo(buf, 0); // valid version
        // sessionInfoOffset (offset 20) left as 0 — below minimum
        using var stream = new MemoryStream(buf);
        Assert.Throws<InvalidDataException>(() => IbtParser.Parse(stream));
    }

    [Fact]
    public void Parse_SessionInfoExceedsFileLength_ThrowsInvalidDataException()
    {
        var buf = new byte[200];
        BitConverter.GetBytes(1).CopyTo(buf, 0);   // valid version
        BitConverter.GetBytes(144).CopyTo(buf, 20); // sessionInfoOffset = 144
        BitConverter.GetBytes(500).CopyTo(buf, 16); // sessionInfoLen = 500, exceeds fileLen
        using var stream = new MemoryStream(buf);
        Assert.Throws<InvalidDataException>(() => IbtParser.Parse(stream));
    }

    [Fact]
    public void Parse_NegativeNumVars_ThrowsInvalidDataException()
    {
        using var stream = FakeIbtBuilder.Build(laps: 0);
        // Overwrite numVars at offset 24 with -1
        var bytes = ((MemoryStream)stream).ToArray();
        BitConverter.GetBytes(-1).CopyTo(bytes, 24);
        using var bad = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => IbtParser.Parse(bad));
    }

    [Fact]
    public void Parse_InvalidSessionDate_ThrowsInvalidDataException()
    {
        // long.MinValue is far outside the DateTimeOffset representable range
        using var stream = FakeIbtBuilder.Build(sessionDate: long.MinValue);
        Assert.Throws<InvalidDataException>(() => IbtParser.Parse(stream));
    }

    [Theory]
    [InlineData(36, int.MaxValue)]
    [InlineData(36, 9)]
    [InlineData(36, -1)]
    [InlineData(52, int.MaxValue)]
    [InlineData(52, -1)]
    public void Parse_InvalidDataBuffer_RejectsBeforeReadingPayload(int headerOffset, int value)
    {
        using var valid = FakeIbtBuilder.Build(laps: 0);
        var bytes = valid.ToArray();
        // The valid file has exactly one eight-byte record. int.MaxValue also exercises
        // arithmetic that would overflow if buffer offset + length were added as int32.
        BitConverter.GetBytes(value).CopyTo(bytes, headerOffset);
        using var stream = new HeaderOnlyStream(bytes);

        var exception = Assert.Throws<InvalidDataException>(() => IbtParser.Parse(stream));

        Assert.Equal("Invalid data buffer configuration in .ibt header.", exception.Message);
    }

    [Fact]
    public void Parse_NonemptyDataBufferAtEndOfFile_RejectsBeforeReadingPayload()
    {
        using var valid = FakeIbtBuilder.Build(laps: 0);
        var bytes = valid.ToArray();
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 52);
        using var stream = new HeaderOnlyStream(bytes);

        var exception = Assert.Throws<InvalidDataException>(() => IbtParser.Parse(stream));

        Assert.Equal("Invalid data buffer configuration in .ibt header.", exception.Message);
    }

    [Fact]
    public void Parse_EmptyDataBufferAtEndOfFile_ReturnsSessionMetadata()
    {
        using var valid = FakeIbtBuilder.Build(includesLapVars: false);
        var bytes = valid.ToArray();
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 52);
        BitConverter.GetBytes(0).CopyTo(bytes, 36);
        using var stream = new MemoryStream(bytes);

        var session = IbtParser.Parse(stream);

        Assert.Equal(42, session.IracingTrackId);
        Assert.Empty(session.Laps);
    }

    [Fact]
    public void Parse_DataBufferEndsExactlyAtEndOfFile_AcceptsCompleteRecord()
    {
        using var stream = FakeIbtBuilder.Build(laps: 0);

        var session = IbtParser.Parse(stream);

        Assert.Equal(42, session.IracingTrackId);
        Assert.Empty(session.Laps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_PartialTrailingRecord_PreservesEarlierCompleteLap(bool inferRecordCount)
    {
        using var valid = FakeIbtBuilder.Build(laps: 2);
        var bytes = valid.ToArray()[..^4];
        if (inferRecordCount)
            BitConverter.GetBytes(0).CopyTo(bytes, 140);
        using var stream = new MemoryStream(bytes);

        var session = IbtParser.Parse(stream);

        var lap = Assert.Single(session.Laps);
        Assert.Equal(1, lap.LapNumber);
        Assert.Equal(90.5, lap.LapTimeSeconds);
        Assert.True(lap.IsValid);
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
        Assert.Equal(25.0f, session.AirTempCelsius, tolerance: 0.05f);
        Assert.Equal(35.0f, session.TrackTempCelsius, tolerance: 0.05f);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("N/A")]
    public void Parse_AbsentConfiguration_NormalizesToEmptyInternalValue(string configName)
    {
        using var stream = FakeIbtBuilder.Build(configName: configName);

        var session = IbtParser.Parse(stream);

        Assert.Equal(string.Empty, session.ConfigName);
    }

    [Fact]
    public void Parse_ValidLaps_ReturnsCorrectLapCountAndMarksAsValid()
    {
        using var stream = FakeIbtBuilder.Build(laps: 3, lapTime: 90.5f, validLaps: true);

        var session = IbtParser.Parse(stream);

        Assert.Equal(3, session.Laps.Count);
        Assert.All(session.Laps, l => Assert.True(l.IsValid));
        Assert.All(session.Laps, l => Assert.Equal(90.5, l.LapTimeSeconds, tolerance: 0.005));
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

    [Theory]
    [InlineData(LapSessionType.Race,        "Race")]
    [InlineData(LapSessionType.Practice,    "Practice")]
    [InlineData(LapSessionType.Qualifying,  "Qualify")]
    [InlineData(LapSessionType.TimeTrial,   "Time Trial")]
    [InlineData(LapSessionType.LoneQualify, "Lone Qualify")]
    public void Parse_EventType_IsDecodedFromYaml(LapSessionType expected, string _)
    {
        using var stream = FakeIbtBuilder.Build(eventType: expected);

        var session = IbtParser.Parse(stream);

        Assert.Equal(expected, session.SessionType);
    }

    [Fact]
    public void Parse_MissingEventType_DefaultsToUnknown()
    {
        using var stream = FakeIbtBuilder.Build(eventType: LapSessionType.Unknown);

        var session = IbtParser.Parse(stream);

        Assert.Equal(LapSessionType.Unknown, session.SessionType);
    }

    [Fact]
    public void Parse_SessionDate_IsDecodedFromUnixTimestamp()
    {
        // Unix timestamp 1_000_000 = 2001-09-08 21:46:40 UTC
        using var stream = FakeIbtBuilder.Build(sessionDate: 1_000_000L);

        var session = IbtParser.Parse(stream);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_000_000), session.SessionDate);
    }

    // ── Helper: wraps a stream and disables seeking ───────────────────────────

    // Fail safely before YAML/variable reads or record-buffer allocation if a malformed
    // header escapes validation. Red runs with int.MaxValue never allocate that buffer.
    private sealed class HeaderOnlyStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override long Seek(long offset, SeekOrigin origin) =>
            offset == 0 && origin == SeekOrigin.Begin
                ? base.Seek(offset, origin)
                : throw new InvalidOperationException("Parser reached payload before rejecting invalid buffer bounds.");
    }

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
