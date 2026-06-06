namespace ApexRacers.Core.Models;

public enum LapSessionType : byte
{
    Unknown     = 0,
    Practice    = 1,
    Qualifying  = 2,
    TimeTrial   = 3,
    Race        = 4,
    LoneQualify = 5,
}
