using ApexRacers.Api.Telemetry;
using ApexRacers.Core.Models;
using ApexRacers.Data;
using Microsoft.EntityFrameworkCore;

namespace ApexRacers.Api.Services;

public class TelemetryUploadService(AppDbContext db)
{
    public async Task<TelemetryUploadResult> ProcessAsync(Stream ibtStream, CancellationToken ct)
    {
        var session = IbtParser.Parse(ibtStream);

        // Resolve or create the UserProfile based on the iRacing customer ID in the file.
        // TODO: Replace with the authenticated user once AuthController is wired up.
        var user = await db.UserProfiles
            .FirstOrDefaultAsync(u => u.IRacingCustomerId == session.DriverCustomerId, ct);

        if (user is null)
        {
            user = new UserProfile
            {
                Id = Guid.NewGuid(),
                IRacingCustomerId = session.DriverCustomerId,
                DisplayName = session.DriverName.Length > 0 ? session.DriverName : $"Driver {session.DriverCustomerId}",
            };
            db.UserProfiles.Add(user);
        }

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

        var validLaps = session.Laps.Where(l => l.IsValid).ToList();

        foreach (var lap in validLaps)
        {
            db.PersonalLaps.Add(new PersonalLap
            {
                UserId          = user.Id,
                CarId           = session.IracingCarId,
                IracingTrackId  = session.IracingTrackId,
                TrackName       = session.TrackName,
                ConfigName      = session.ConfigName,
                LapTimeSeconds  = lap.LapTimeSeconds,
                IsValidLap      = true,
                AirTempCelsius  = session.AirTempCelsius,
                TrackTempCelsius = session.TrackTempCelsius,
                TrackWetness    = session.TrackWetness,
                RecordedAt      = session.SessionDate,
            });
        }

        await db.SaveChangesAsync(ct);

        return new TelemetryUploadResult(
            TotalLaps: session.Laps.Count,
            ValidLaps: validLaps.Count,
            BestLapSeconds: validLaps.Count > 0
                ? validLaps.Min(l => l.LapTimeSeconds)
                : null,
            TrackName:   session.TrackName,
            ConfigName:  session.ConfigName,
            CarName:     session.CarName,
            CustomerId:  session.DriverCustomerId,
            DriverName:  session.DriverName
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
