namespace ApexRacers.Api.Services;

/// <summary>
/// Shared iRacing catalog image-URL construction for the car/track mappers. iRacing returns
/// a folder (e.g. "/img/cars/x") plus a file name, or an already site-relative path (logos);
/// both resolve against the static image host.
/// </summary>
internal static class CatalogImage
{
    public const string Base = "https://images-static.iracing.com";

    /// <summary>base + folder + "/" + file. Null when either part is absent.</summary>
    public static string? FromFolder(string? folder, string? file) =>
        string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(file) ? null : $"{Base}{folder}/{file}";

    /// <summary>base + a site-relative path (e.g. a logo "/img/logos/..."). Null when absent.</summary>
    public static string? FromPath(string? path) =>
        string.IsNullOrEmpty(path) ? null : $"{Base}{path}";
}
