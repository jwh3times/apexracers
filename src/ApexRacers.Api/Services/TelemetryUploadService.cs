using ApexRacers.Api.Telemetry;
using ApexRacers.Core;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class TelemetryUploadService(AppDbContext db)
{
    public async Task<TelemetryUploadResult> ProcessAsync(Stream ibtStream, Guid userId, CancellationToken ct)
    {
        var session = IbtParser.Parse(ibtStream);

        // A file that names no driver parses to 0 — treat that as "not established" rather than
        // as customer 0, so it is never compared against or stored as a real Customer ID.
        var recordedBy = session.DriverCustomerId > 0 ? session.DriverCustomerId : (long?)null;

        var claimedCustId = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.IRacingCustomerId)
            .FirstOrDefaultAsync(ct);

        // Reject before anything is written. An accepted upload's laps become the uploader's own
        // pace and are ranked against a field of real race laps, so telemetry driven by someone
        // else must not reach the database at all.
        // A caller with no Claimed Identity has nothing to check against and is let through; the
        // recording Driver is still stored, so the lap says whose it is either way.
        if (recordedBy is { } fileCustId && claimedCustId is { } claimed && fileCustId != claimed)
        {
            throw new InvalidOperationException(
                $"This telemetry was recorded by driver {fileCustId}, not by the iRacing account " +
                $"linked to your profile ({claimed}). Upload telemetry you drove, or update your " +
                "linked Customer ID in Settings.");
        }

        // Catalog ingestion and seeding own Car/Track metadata. An uploaded recording may
        // reference existing IDs, but its untrusted YAML must never create public catalog rows.
        if (!await db.Cars.AnyAsync(car => car.Id == session.IracingCarId, ct) ||
            !await db.Tracks.AnyAsync(track => track.Id == session.IracingTrackId, ct))
            throw new InvalidOperationException(
                "This telemetry's car or track is not in the catalog yet. Try again after the catalog is updated.");

        var validLaps = session.Laps.Where(l => l.IsValid).ToList();

        // Deduplicate at the session level: re-uploading the same .ibt must not insert
        // its laps again. A session is identified by user + car + track + session start
        // timestamp; if any lap from it is already persisted, skip the insert entirely.
        // (Keying on individual lap times instead would collapse legitimately-repeated
        // identical times within a single session.)
        var recordedAt = session.SessionDate;
        var alreadyImported = await db.UploadedLaps.AnyAsync(p =>
            p.UserId == userId
            && p.CarId == session.IracingCarId
            && p.TrackId == session.IracingTrackId
            && p.RecordedAt == recordedAt, ct);

        if (!alreadyImported)
        {
            foreach (var lap in validLaps)
            {
                db.UploadedLaps.Add(new UploadedLap
                {
                    UserId           = userId,
                    DriverCustId     = recordedBy,
                    CarId            = session.IracingCarId,
                    TrackId          = session.IracingTrackId,
                    LapTimeSeconds   = lap.LapTimeSeconds,
                    SessionType      = session.SessionType,
                    AirTempCelsius   = session.AirTempCelsius,
                    TrackTempCelsius = session.TrackTempCelsius,
                    TrackWetness     = session.TrackWetness,
                    RecordedAt       = recordedAt,
                });
            }
        }

        await db.SaveChangesAsync(ct);

        return new TelemetryUploadResult(
            TotalLaps: session.Laps.Count,
            ValidLaps: validLaps.Count,
            BestLapSeconds: validLaps.Count > 0
                ? validLaps.Min(l => l.LapTimeSeconds)
                : null,
            TrackName:  session.TrackName,
            ConfigName: ConfigurationName.Normalize(session.ConfigName),
            CarName:    session.CarName,
            CustomerId: session.DriverCustomerId,
            DriverName: session.DriverName
        );
    }

    public record TelemetryUploadResult(
        int TotalLaps,
        int ValidLaps,
        double? BestLapSeconds,
        string TrackName,
        string ConfigName,
        string CarName,
        long CustomerId,
        string DriverName);
}
