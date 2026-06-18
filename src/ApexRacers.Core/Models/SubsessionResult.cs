namespace ApexRacers.Core.Models;

public class SubsessionResult
{
    public int SubsessionId { get; set; }
    public long CustId { get; set; }
    public int CarId { get; set; }
    public int CarClassId { get; set; }
    public int FinishPosition { get; set; }
    public int FinishPositionInClass { get; set; }
    public int StartingPosition { get; set; }
    public int StartingPositionInClass { get; set; }
    public int Incidents { get; set; }
    public double BestLapSeconds { get; set; }      // seconds; -1 = no valid lap
    public double AverageLapSeconds { get; set; }   // seconds; -1 = none
    public int LapsComplete { get; set; }
    public int LapsLead { get; set; }
    public int ChampPoints { get; set; }
    public int AggregateChampPoints { get; set; }
    public int NewIRating { get; set; }
    public int OldIRating { get; set; }
    public double NewCpi { get; set; }
    public double OldCpi { get; set; }
    public string? ReasonOut { get; set; }
    public int ReasonOutId { get; set; }
    public int Division { get; set; }
    public bool DropRace { get; set; }
    public double Interval { get; set; }            // seconds gap to class leader; -1 = N/A

    // Race-detail context (1.3).
    public string? DisplayName { get; set; }        // driver display name
    public double QualLapSeconds { get; set; }      // seconds; -1 = none
    public int NewSubLevel { get; set; }            // safety rating × 100 after the race
    public int OldSubLevel { get; set; }            // safety rating × 100 before the race
    public int NewTtRating { get; set; }
    public int OldTtRating { get; set; }

    public Subsession Subsession { get; set; } = null!;
    public Car Car { get; set; } = null!;
    public CarClass CarClass { get; set; } = null!;
}
