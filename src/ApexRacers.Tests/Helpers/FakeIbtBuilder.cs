using System.Text;
using ApexRacers.Core.Models;

namespace ApexRacers.Tests.Helpers;

/// <summary>
/// Constructs minimal valid iRacing .ibt binary streams for unit tests.
/// </summary>
internal static class FakeIbtBuilder
{
    private const int HeaderSize    = 144; // irsdk_header + irsdk_diskSubHeader
    private const int VarHeaderSize = 144;

    /// <summary>
    /// Builds a seekable MemoryStream containing a syntactically valid .ibt file.
    /// </summary>
    /// <param name="laps">Number of completed laps to emit.</param>
    /// <param name="lapTime">Lap time (seconds) written to each completed lap record.</param>
    /// <param name="validLaps">When false, writes a negative lapTime so IsValid=false.</param>
    /// <param name="includesLapVars">When false, omits LapCompleted/LapLastLapTime vars → empty laps list.</param>
    public static MemoryStream Build(
        int            laps           = 2,
        float          lapTime        = 90.5f,
        bool           validLaps      = true,
        bool           includesLapVars = true,
        int            trackId        = 42,
        string         trackName      = "Spa-Francorchamps",
        string         configName     = "Full",
        int            carId          = 99,
        string         carName        = "Porsche 992 GT3",
        string         carNameShort   = "P992",
        long           customerId     = 12345,
        string         driverName     = "Jerry Holland",
        long           sessionDate    = 0,
        LapSessionType eventType      = LapSessionType.Unknown)
    {
        var yamlBytes = Encoding.UTF8.GetBytes(BuildYaml(
            trackId, trackName, configName, carId, carName, carNameShort, customerId, driverName, eventType));

        int numVars           = includesLapVars ? 2 : 0;
        int sessionInfoLen    = yamlBytes.Length;
        int sessionInfoOffset = HeaderSize;
        int varHeaderOffset   = sessionInfoOffset + sessionInfoLen;
        int bufLen            = includesLapVars ? 8 : 0; // LapCompleted(int32) + LapLastLapTime(float)
        int firstBufOffset    = varHeaderOffset + numVars * VarHeaderSize;

        // N laps requires N+1 records:
        //   rec 0: LapCompleted=0  → sets prevLapCompleted; no lap emitted
        //   rec r: LapCompleted=r  → lap r emitted
        int sessionRecordCount = includesLapVars ? laps + 1 : 1;
        int totalSize          = firstBufOffset + sessionRecordCount * Math.Max(bufLen, 1);

        var buf = new byte[totalSize];

        // ── Main header ───────────────────────────────────────────────────────
        WriteI32(buf,  0, 1);                 // version = 1
        WriteI32(buf, 16, sessionInfoLen);
        WriteI32(buf, 20, sessionInfoOffset);
        WriteI32(buf, 24, numVars);
        WriteI32(buf, 28, varHeaderOffset);
        WriteI32(buf, 36, bufLen > 0 ? bufLen : 1);
        WriteI32(buf, 52, firstBufOffset);    // varBuf[0].bufOffset
        WriteI64(buf, 112, sessionDate);      // sessionStartDate (int64/time_t)
        WriteI32(buf, 140, sessionRecordCount);

        // ── YAML session info ─────────────────────────────────────────────────
        Array.Copy(yamlBytes, 0, buf, sessionInfoOffset, yamlBytes.Length);

        if (includesLapVars)
        {
            // ── Var header 0: LapCompleted (int32) at record offset 0 ─────────
            WriteI32(buf,  varHeaderOffset + 4,  0);
            WriteAscii(buf, varHeaderOffset + 16, "LapCompleted", 32);

            // ── Var header 1: LapLastLapTime (float) at record offset 4 ──────
            int vh1 = varHeaderOffset + VarHeaderSize;
            WriteI32(buf,  vh1 + 4,  4);
            WriteAscii(buf, vh1 + 16, "LapLastLapTime", 32);

            // ── Data records ──────────────────────────────────────────────────
            for (int r = 0; r <= laps; r++)
            {
                int recOffset = firstBufOffset + r * bufLen;
                WriteI32(buf, recOffset, r);                                      // LapCompleted
                float lt = r > 0 ? (validLaps ? lapTime : -1.0f) : 0f;
                WriteF32(buf, recOffset + 4, lt);                                 // LapLastLapTime
            }
        }

        return new MemoryStream(buf);
    }

    private static string EventTypeString(LapSessionType t) => t switch
    {
        LapSessionType.Race        => "Race",
        LapSessionType.Practice    => "Practice",
        LapSessionType.Qualifying  => "Qualify",
        LapSessionType.TimeTrial   => "Time Trial",
        LapSessionType.LoneQualify => "Lone Qualify",
        _                          => "",
    };

    private static string BuildYaml(
        int trackId, string trackName, string configName,
        int carId, string carName, string carNameShort,
        long customerId, string driverName,
        LapSessionType eventType = LapSessionType.Unknown)
    {
        var eventTypeLine = eventType != LapSessionType.Unknown
            ? $"\n EventType: {EventTypeString(eventType)}"
            : "";
        return $"""
        ---
        WeekendInfo:
         TrackID: {trackId}
         TrackDisplayName: {trackName}
         TrackConfigName: {configName}
         AirTemp: 25.0 C
         TrackTemp: 35.0 C{eventTypeLine}
        DriverInfo:
         DriverCarIdx: 0
         DriverUserID: {customerId}
         Drivers:
         - CarIdx: 0
           CarID: {carId}
           CarScreenName: {carName}
           CarScreenNameShort: {carNameShort}
           UserName: {driverName}
        ...
        """;
    }

    private static void WriteI32(byte[] b, int o, int v)    => BitConverter.GetBytes(v).CopyTo(b, o);
    private static void WriteI64(byte[] b, int o, long v)   => BitConverter.GetBytes(v).CopyTo(b, o);
    private static void WriteF32(byte[] b, int o, float v)  => BitConverter.GetBytes(v).CopyTo(b, o);

    private static void WriteAscii(byte[] b, int o, string s, int maxLen)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        Array.Copy(bytes, 0, b, o, Math.Min(bytes.Length, maxLen));
    }
}
