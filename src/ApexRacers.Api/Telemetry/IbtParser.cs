using System.Text;

namespace ApexRacers.Api.Telemetry;

/// <summary>
/// Parses iRacing .ibt binary telemetry disk files.
///
/// File layout:
///   [  0] irsdk_header        — 112 bytes (ver, tick rate, session info offset, var headers, buf descriptors)
///   [112] irsdk_diskSubHeader — 32 bytes  (session date, record count)
///   [sessionInfoOffset]       — YAML session info (track, car, driver, weather)
///   [varHeaderOffset]         — irsdk_varHeader[numVars], 144 bytes each (channel descriptors)
///   [varBuf[0].bufOffset]     — data records, bufLen bytes each × sessionRecordCount
/// </summary>
public static class IbtParser
{
    private const int VarHeaderSize = 144;

    public static ParsedIbtSession Parse(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        // ── 1. Read irsdk_header + irsdk_diskSubHeader (144 bytes total) ─────────
        var hdr = new byte[144];
        stream.Seek(0, SeekOrigin.Begin);
        stream.ReadExactly(hdr);

        int ver = RI32(hdr, 0);
        if (ver < 1 || ver > 2)
            throw new InvalidDataException($"Unsupported iRacing telemetry version {ver}. Expected 1 or 2.");

        int sessionInfoLen    = RI32(hdr, 16);
        int sessionInfoOffset = RI32(hdr, 20);
        int numVars           = RI32(hdr, 24);
        int varHeaderOffset   = RI32(hdr, 28);
        int bufLen            = RI32(hdr, 36);

        // varBuf[0] starts at offset 48: tickCount(4) | bufOffset(4) | pad(8)
        int firstBufOffset = RI32(hdr, 52);

        // Disk sub-header at offset 112
        // sessionStartDate is time_t — int64 on 64-bit Windows, not a double
        long sessionStartDate  = RL64(hdr, 112);
        int sessionRecordCount = RI32(hdr, 140);

        // Fall back to computing record count from file length if sub-header is missing
        if (sessionRecordCount <= 0 && bufLen > 0)
            sessionRecordCount = (int)((stream.Length - firstBufOffset) / bufLen);

        // ── 2. Session YAML ───────────────────────────────────────────────────────
        stream.Seek(sessionInfoOffset, SeekOrigin.Begin);
        var yamlBytes = new byte[sessionInfoLen];
        stream.ReadExactly(yamlBytes);
        var yaml = Encoding.UTF8.GetString(yamlBytes).TrimEnd('\0');
        var info = ParseYaml(yaml);

        // ── 3. Variable headers ───────────────────────────────────────────────────
        stream.Seek(varHeaderOffset, SeekOrigin.Begin);
        var varBuf = new byte[numVars * VarHeaderSize];
        stream.ReadExactly(varBuf);

        int lapCompletedOff   = -1;
        int lapLastLapTimeOff = -1;
        int airTempOff        = -1;
        int trackTempOff      = -1;
        int trackWetnessOff   = -1;

        for (int i = 0; i < numVars; i++)
        {
            int b = i * VarHeaderSize;
            int off = RI32(varBuf, b + 4);
            var name = Encoding.ASCII.GetString(varBuf, b + 16, 32).TrimEnd('\0');
            switch (name)
            {
                case "LapCompleted":   lapCompletedOff   = off; break;
                case "LapLastLapTime": lapLastLapTimeOff = off; break;
                case "AirTemp":        airTempOff        = off; break;
                case "TrackTemp":      trackTempOff      = off; break;
                case "TrackWetness":   trackWetnessOff   = off; break;
            }
        }

        // ── 4. Scan data records ──────────────────────────────────────────────────
        float airTemp   = info.AirTempCelsius;
        float trackTemp = info.TrackTempCelsius;
        byte  wetness   = 0;
        var laps = new List<ParsedLap>();

        if (lapCompletedOff >= 0 && lapLastLapTimeOff >= 0 && sessionRecordCount > 0)
        {
            stream.Seek(firstBufOffset, SeekOrigin.Begin);
            var rec = new byte[bufLen];
            int prevLapCompleted = -1;
            bool condRead = false;

            for (int r = 0; r < sessionRecordCount; r++)
            {
                // Partial record at end of an interrupted session — stop cleanly
                int n = ReadFull(stream, rec);
                if (n < bufLen) break;

                if (!condRead)
                {
                    if (airTempOff >= 0)      airTemp  = RFloat(rec, airTempOff);
                    if (trackTempOff >= 0)    trackTemp = RFloat(rec, trackTempOff);
                    if (trackWetnessOff >= 0) wetness  = (byte)Math.Clamp(RI32(rec, trackWetnessOff), 0, 7);
                    condRead = true;
                }

                int lapCompleted = RI32(rec, lapCompletedOff);

                if (prevLapCompleted >= 0 && lapCompleted > prevLapCompleted)
                {
                    float lapTime = RFloat(rec, lapLastLapTimeOff);
                    laps.Add(new ParsedLap
                    {
                        LapNumber = lapCompleted,
                        LapTimeSeconds = lapTime,
                        IsValid = lapTime > 0f,
                    });
                }

                prevLapCompleted = lapCompleted;
            }
        }

        return new ParsedIbtSession
        {
            IracingTrackId     = info.TrackId,
            TrackName          = info.TrackName,
            ConfigName         = info.ConfigName,
            IracingCarId       = info.CarId,
            CarName            = info.CarName,
            CarNameAbbreviated = info.CarNameShort.Length > 0 ? info.CarNameShort : info.CarName,
            DriverCustomerId   = info.DriverCustomerId,
            DriverName         = info.DriverName,
            AirTempCelsius     = airTemp,
            TrackTempCelsius   = trackTemp,
            TrackWetness       = wetness,
            SessionDate        = DateTimeOffset.FromUnixTimeSeconds(sessionStartDate),
            Laps               = laps,
        };
    }

    // ── YAML parsing ─────────────────────────────────────────────────────────────
    //
    // iRacing session info YAML is a subset that we parse line-by-line.
    // Indentation uses single spaces; driver array items are prefixed with "- ".

    private record YamlInfo(
        int TrackId, string TrackName, string ConfigName,
        int CarId, string CarName, string CarNameShort,
        long DriverCustomerId, string DriverName,
        float AirTempCelsius, float TrackTempCelsius);

    private static YamlInfo ParseYaml(string yaml)
    {
        int    trackId          = 0;
        string trackName        = "";
        string configName       = "";
        int    carId            = 0;
        string carName          = "";
        string carNameShort     = "";
        long   driverCustomerId = 0;
        string driverName       = "";
        float  airTemp          = 0f;
        float  trackTemp        = 0f;

        int  driverCarIdx   = 0;
        bool inDrivers      = false;
        int  currentCarIdx  = -1;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var line    = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            // Array items in the Drivers list start with "- "; strip that prefix
            // so subsequent key parsing is uniform.
            if (inDrivers && trimmed.StartsWith("- "))
            {
                trimmed = trimmed[2..];
                // A new "- CarIdx:" signals the start of a new driver entry
            }

            if      (YamlVal(trimmed, "TrackID:",           out var v)) int.TryParse(v, out trackId);
            else if (YamlVal(trimmed, "TrackDisplayName:",   out v))     trackName = v;
            else if (YamlVal(trimmed, "TrackConfigName:",    out v))     configName = v;
            else if (YamlVal(trimmed, "AirTemp:",            out v))     airTemp = ParseTemp(v);
            else if (YamlVal(trimmed, "TrackTemp:",          out v))     trackTemp = ParseTemp(v);
            else if (YamlVal(trimmed, "DriverCarIdx:",       out v))     int.TryParse(v, out driverCarIdx);
            else if (YamlVal(trimmed, "DriverUserID:",       out v) && !inDrivers) long.TryParse(v, out driverCustomerId);
            else if (trimmed == "Drivers:")                              inDrivers = true;
            else if (inDrivers && YamlVal(trimmed, "CarIdx:", out v))   int.TryParse(v, out currentCarIdx);
            else if (inDrivers && currentCarIdx == driverCarIdx)
            {
                if      (YamlVal(trimmed, "CarID:",              out v)) int.TryParse(v, out carId);
                else if (YamlVal(trimmed, "CarScreenName:",      out v)) carName = v;
                else if (YamlVal(trimmed, "CarScreenNameShort:", out v)) carNameShort = v;
                else if (YamlVal(trimmed, "UserName:",           out v)) driverName = v;
            }
        }

        return new YamlInfo(trackId, trackName, configName,
                            carId, carName, carNameShort,
                            driverCustomerId, driverName,
                            airTemp, trackTemp);
    }

    private static bool YamlVal(string line, string key, out string value)
    {
        if (line.StartsWith(key, StringComparison.Ordinal))
        {
            value = line[key.Length..].Trim();
            return true;
        }
        value = "";
        return false;
    }

    // Parses "26.11 C" or "26.11" → 26.11f
    private static float ParseTemp(string val)
    {
        var end = val.IndexOf(' ');
        var num = end >= 0 ? val[..end] : val;
        return float.TryParse(num, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var r) ? r : 0f;
    }

    // ── Binary helpers (little-endian) ────────────────────────────────────────

    private static int    RI32   (byte[] b, int o) => BitConverter.ToInt32  (b, o);
    private static long   RL64   (byte[] b, int o) => BitConverter.ToInt64  (b, o);
    private static float  RFloat (byte[] b, int o) => BitConverter.ToSingle (b, o);

    // Reads exactly buf.Length bytes, handling partial reads from buffered streams.
    // Returns the number of bytes actually read (< buf.Length only at end of stream).
    private static int ReadFull(Stream s, byte[] buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf, total, buf.Length - total);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
