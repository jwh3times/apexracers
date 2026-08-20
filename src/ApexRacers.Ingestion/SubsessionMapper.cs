using System.Text.Json;
using ApexRacers.Core.Models;
using Aydsko.iRacingData.Results;
using CoreSubsessionResult = ApexRacers.Core.Models.SubsessionResult;
using WireSubsessionResult = Aydsko.iRacingData.Results.SubSessionResult;

namespace ApexRacers.Ingestion;

/// <summary>
/// The persistence seam for the SDK's subsession result shapes. Keeping the field mapping here
/// makes SDK drift a compile-time failure in covered code while the worker remains an I/O shell.
/// </summary>
public static class SubsessionMapper
{
    public static Subsession ToEntity(
        int subsessionId,
        WireSubsessionResult data,
        Guid? weekId,
        SubsessionIndexer.SplitPosition? splitPosition) =>
        new()
        {
            Id = subsessionId,
            SeasonId = data.SeasonId,
            WeekNumber = data.RaceWeekIndex,
            WeekId = weekId,
            TrackId = data.Track.TrackId,
            OfficialSession = data.OfficialSession,
            EventStrengthOfField = data.EventStrengthOfField,
            StartTime = data.StartTime,
            EndTime = data.EndTime,
            SplitIndex = splitPosition?.Index,
            SplitCount = splitPosition?.Count,
            NumCautions = data.NumberOfCautions,
            NumCautionLaps = data.NumberOfCautionLaps,
            NumLeadChanges = data.NumberOfLeadChanges,
            CornersPerLap = data.CornersPerLap,
            EventAverageLapSeconds = SubsessionIndexer.EventLapSecondsOrSentinel(data.EventAverageLap),
            EventBestLapSeconds = SubsessionIndexer.EventLapSecondsOrSentinel(data.EventBestLapTime),
            EventLapsComplete = data.EventLapsComplete,
            WeatherJson = WeatherIngest.ToSnapshot(data.Weather) is { } weather
                ? JsonSerializer.Serialize(weather)
                : null,
            TrackStateJson = TrackStateIngest.ToSnapshot(data.TrackState) is not { } trackState
                ? null
                : JsonSerializer.Serialize(trackState),
        };

    public static CoreSubsessionResult ToResult(int subsessionId, Result result) =>
        new()
        {
            SubsessionId = subsessionId,
            CustId = result.CustomerId!.Value,
            CarId = result.CarId,
            CarClassId = result.CarClassId,
            FinishPosition = result.FinishPosition,
            FinishPositionInClass = result.FinishPositionInClass,
            StartingPosition = result.StartingPosition,
            StartingPositionInClass = result.StartingPositionInClass ?? 0,
            Incidents = result.Incidents,
            BestLapSeconds = SubsessionIndexer.LapSecondsOrSentinel(result.BestLapTime),
            AverageLapSeconds = SubsessionIndexer.LapSecondsOrSentinel(result.AverageLap),
            LapsComplete = result.LapsComplete,
            LapsLead = result.LapsLead,
            ChampPoints = result.ChampPoints,
            AggregateChampPoints = result.AggregateChampionshipPoints,
            NewIRating = result.NewIRating,
            OldIRating = result.OldIRating,
            NewCpi = (double)result.NewCornersPerIncident,
            OldCpi = (double)result.OldCornersPerIncident,
            ReasonOut = result.ReasonOut,
            ReasonOutId = result.ReasonOutId,
            Division = result.Division,
            DropRace = result.DropRace,
            Interval = result.ClassInterval?.TotalSeconds ?? -1,
            DisplayName = result.DisplayName,
            QualLapSeconds = SubsessionIndexer.LapSecondsOrSentinel(result.QualifyingLapTime),
            NewSubLevel = result.NewSubLevel,
            OldSubLevel = result.OldSubLevel,
            NewTtRating = result.NewTimeTrialRating,
            OldTtRating = result.OldTimeTrialRating,
        };
}
