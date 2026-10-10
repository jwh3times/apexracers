using ApexRacers.Core;

namespace ApexRacers.Data;

/// <summary>Shared API/background acquisition interface. Work consumes its declared unit before
/// invoking the adapter; failed, canceled and uncertain fetches receive no refund. It does not
/// equate one work unit with one provider HTTP request or grant collection purpose/Driver consent.</summary>
public sealed class DriverOperatingCollection(IDriverOperatingControls controls)
{
    public async Task<T> CollectAsync<T>(OperatingRequest request, Func<CancellationToken, Task<T>> fetch,
        CancellationToken ct = default)
    {
        if (request.Work is not (OperatingWork.Acquisition or OperatingWork.BackgroundCollection))
            throw new EvidenceCopyUnavailableException();
        var lease = await controls.ReserveAsync(request, ct) ?? throw new EvidenceCopyUnavailableException();
        if (!await controls.CurrentAsync(lease, ct)) throw new EvidenceCopyUnavailableException();
        var value = await fetch(ct);
        if (!await controls.CurrentAsync(lease, ct)) throw new EvidenceCopyUnavailableException();
        return value;
    }
}
