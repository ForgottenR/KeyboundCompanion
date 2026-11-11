namespace KeyboundClick;

internal enum CandidateRole
{
    Unknown = -1,
    Normal,
    Boss,
    MagicPot,
    Barrier,
}

internal sealed record Candidate(
    uint EntityId,
    uint DataId,
    string Label,
    string Word,
    uint Kind,
    bool IsBoss,
    CandidateRole Role = CandidateRole.Normal,
    float HealthPercent = 100,
    float ScreenY = 0,
    float CastProgress = 0,
    uint CastActionId = 0,
    float CastRemainingSeconds = 0,
    string CastAction = "",
    bool IsDead = false)
{
    internal bool IsCasting => CastActionId != 0 || CastProgress > 0;
}

internal sealed record CastSnapshot(uint EntityId, uint ActionId, string Name, float CurrentSeconds, float TotalSeconds)
{
    internal float RemainingSeconds => Math.Max(0, TotalSeconds - CurrentSeconds);
    internal float Progress => TotalSeconds <= 0 ? 0 : Math.Clamp(CurrentSeconds / TotalSeconds, 0, 1);
}

internal sealed record GameSnapshot(
    int Stage,
    uint Score,
    uint ScoreBonusTime,
    bool IsFever,
    bool InputAllowed,
    int InputLockSeconds,
    string InputStatus,
    string InputText,
    long InputSession,
    float IdleSeconds,
    Candidate[] Candidates,
    CastSnapshot[] HaltCasts);
