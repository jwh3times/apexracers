using ApexRacers.Api.Telemetry;
using ApexRacers.Core;
using ApexRacers.Data;

namespace ApexRacers.Api.Services;

/// <summary>Owns the submitted stream for this operation. Ordinary uploads can only produce a
/// transient non-identifying preview. The controlled synthetic seam requires current proof/consent
/// before parsing and rechecks the captured generation when committing typed personal laps.</summary>
public sealed class TelemetryUploadService(AppDbContext db, DriverAuthorization? authority = null,
    PrivateUploadStore? privateUploads = null)
{
    public Task<PrivateUploadOutcome> ProcessAsync(Stream ibtStream, Guid userId, CancellationToken ct = default) =>
        ProcessCoreAsync(ibtStream, null, ct);

    public Task<PrivateUploadOutcome> ProcessSyntheticAsync(Stream ibtStream, DriverScope owner, CancellationToken ct = default) =>
        ProcessCoreAsync(ibtStream, owner, ct);

    private async Task<PrivateUploadOutcome> ProcessCoreAsync(Stream ibtStream, DriverScope? owner, CancellationToken ct)
    {
        await using (ibtStream)
        {
            ct.ThrowIfCancellationRequested();
            PrivateUploadReceipt? receipt = null;
            if (owner is not null)
            {
                if (db.Provenance != DataProvenance.Demo || owner.Provenance != DataProvenance.Demo
                    || authority is null || privateUploads is null
                    || await authority.ResolveAsync(owner, DriverConsentScope.Personal, ct) is not { } access)
                    throw new EvidenceCopyUnavailableException();
                receipt = await privateUploads.CaptureAsync(access, ct);
            }
            if (!ibtStream.CanSeek || ibtStream.Length > TelemetryUpload.MaxFileSizeBytes)
                throw new ArgumentException("The telemetry stream is unsupported or exceeds the upload limit.");
            ParsedIbtSession session;
            try { session = IbtParser.Parse(ibtStream); }
            catch (InvalidDataException) { throw new ArgumentException("The telemetry file could not be processed."); }
            ct.ThrowIfCancellationRequested();
            if (owner is not null && session.DriverCustomerId != owner.CustomerId)
                throw new InvalidOperationException("This telemetry cannot be attributed to this account.");
            var laps = session.Laps.Where(l => l.IsValid && double.IsFinite(l.LapTimeSeconds) && l.LapTimeSeconds > 0).ToList();
            if (receipt is not null && laps.Count != 0)
                await privateUploads!.CommitAsync(receipt, new(session.IracingCarId, session.IracingTrackId,
                    session.SessionDate, session.SessionType, laps.Select(l => new PrivateLap(l.LapNumber, l.LapTimeSeconds)).ToArray()), ct);
            // No recorder ID, name, recording YAML, raw file or untrusted catalog label leaves this seam.
            return new(receipt is not null && laps.Count != 0, session.Laps.Count, laps.Count,
                laps.Count == 0 ? null : laps.Min(l => l.LapTimeSeconds));
        }
    }
}
