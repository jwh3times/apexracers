using System.Security.Cryptography;
using System.Text;

namespace ApexRacers.Core.PercentileHistory;

/// <summary>One stable classified derivative identity. Grant revision is checked metadata, never
/// a second contribution. Identifier/key values establish no collection, persistence or read right.</summary>
public static class PercentileHistoryKey
{
    public static string Create(PercentileOwnerIdentity owner, Guid? grantId, PercentileSelection selection,
        int carId, int seriesId, Guid weekId)
    {
        if (owner.UserId == Guid.Empty || owner.CustomerId <= 0 || owner.CustomerId > int.MaxValue
            || owner.Provenance is not (DataProvenance.Real or DataProvenance.Demo)
            || carId <= 0 || seriesId <= 0 || weekId == Guid.Empty || selection is null
            || (owner.Kind switch
            {
                PercentileOwnerKind.DemoSubject => owner.Provenance != DataProvenance.Demo
                    || owner.CustomerId != DemoData.DriverCustId || grantId is not null,
                PercentileOwnerKind.Personal => grantId is null || grantId == Guid.Empty,
                _ => true,
            }))
            throw new ArgumentException("A classified percentile identity is required.");
        return FormattableString.Invariant($"percentile-history:v1:{(int)owner.Provenance}:{owner.UserId:N}:{owner.CustomerId}:{(int)owner.Kind}:{grantId?.ToString("N") ?? "-"}:{carId}:{seriesId}:{weekId:N}:{selection.Key}");
    }

    public static string Hash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
}
