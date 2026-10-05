namespace ApexRacers.Core.Models;

public class EvidenceCopyDependency
{
    public Guid CopyId { get; set; }
    public Guid SourceCopyId { get; set; }
    public long SourceVersion { get; set; }
}
