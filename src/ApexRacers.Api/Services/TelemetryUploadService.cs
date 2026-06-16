using ApexRacers.Api.Telemetry;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class TelemetryUploadService(AppDbContext db)
{
    public async Task<TelemetryUploadResult> ProcessAsync(Stream ibtStream, Guid userId, CancellationToken ct)
    {
        var session = IbtParser.Parse(ibtStream);

        // Upsert the car — the ingestion worker is the authoritative source but
        // telemetry files can arrive before ingestion has run.
        var car = await db.Cars.FindAsync([session.IracingCarId], ct);
        if (car is null)
        {
            car = new Car
            {
                Id = session.IracingCarId,
                Name = session.CarName,
                NameAbbreviated = session.CarNameAbbreviated,
            };
            db.Cars.Add(car);
        }

        // Upsert the track — the ingestion worker is the authoritative source but
        // telemetry files can arrive before ingestion has run.
        var track = await db.Tracks.FindAsync([session.IracingTrackId], ct);
        if (track is null)
        {
            track = new Track
            {
                Id         = session.IracingTrackId,
                Name       = session.TrackName,
                ConfigName = session.ConfigName,
            };
            db.Tracks.Add(track);
        }

        var validLaps = session.Laps.Where(l => l.IsValid).ToList();

        // Deduplicate at the session level: re-uploading the same .ibt must not insert
        // its laps again. A session is identified by user + car + track + session start
        // timestamp; if any lap from it is already persisted, skip the insert entirely.
        // (Keying on individual lap times instead would collapse legitimately-repeated
        // identical times within a single session.)
        var recordedAt = session.SessionDate;
        var alreadyImported = await db.PersonalLaps.AnyAsync(p =>
            p.UserId == userId
            && p.CarId == session.IracingCarId
            && p.TrackId == session.IracingTrackId
            && p.RecordedAt == recordedAt, ct);

        if (!alreadyImported)
        {
            foreach (var lap in validLaps)
            {
                db.PersonalLaps.Add(new PersonalLap
                {
                    UserId           = userId,
                    CarId            = session.IracingCarId,
                    TrackId          = session.IracingTrackId,
                    LapTimeSeconds   = lap.LapTimeSeconds,
                    IsValidLap       = true,
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
            ConfigName: session.ConfigName,
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
