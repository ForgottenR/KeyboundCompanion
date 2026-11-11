namespace KeyboundClick;

internal sealed class SafetyGate
{
    internal const int DefaultPreCastMs = 1800;
    internal const int DefaultPostCastMs = 3500;

    private bool haltLanding;
    private long holdStartedAt = -1;
    private string lastHalt = string.Empty;

    internal string? Evaluate(long now, GameSnapshot snapshot, int preCastMs = DefaultPreCastMs, int postCastMs = DefaultPostCastMs)
    {
        var preCastSeconds = preCastMs / 1000f;
        var landing = false;
        foreach (var cast in snapshot.HaltCasts)
        {
            if (cast.RemainingSeconds > preCastSeconds && cast.Progress < 0.95f) continue;
            landing = true;
            haltLanding = true;
            lastHalt = cast.Name;
            holdStartedAt = -1;
            break;
        }

        if (landing) return $"停止输入：{lastHalt} 即将生效";

        if (haltLanding && snapshot.HaltCasts.Length == 0)
        {
            haltLanding = false;
            holdStartedAt = now;
        }

        var holdUntil = holdStartedAt < 0 ? 0 : holdStartedAt + postCastMs;
        if (now < holdUntil) return $"停止输入：等待 {lastHalt} 效果结束（{(holdUntil - now + 999) / 1000} 秒）";
        if (holdStartedAt >= 0) holdStartedAt = -1;
        if (snapshot.InputLockSeconds > 0) return $"当前禁止输入（{snapshot.InputLockSeconds} 秒）";
        if (!snapshot.InputAllowed) return snapshot.InputStatus;
        return null;
    }

    internal void Reset()
    {
        haltLanding = false;
        holdStartedAt = -1;
        lastHalt = string.Empty;
    }
}
