namespace KeyboundClick;

internal interface ISubmissionGateway
{
    bool Submit(string word);
}

internal enum SubmissionEventKind
{
    Attempt,
    Effect,
    Unmatched,
    Stall,
    Fault,
}

internal sealed record SubmissionEvent(
    SubmissionEventKind Kind,
    long Now,
    Candidate? Target,
    bool Matched,
    uint Score,
    float Health,
    string Reason);

internal sealed class SubmissionCoordinator(SubmissionCooldown cooldown, IntervalSchedule schedule)
{
    private const int EffectTimeoutMs = 1000;
    private const int DisappearConfirmationMs = 100;
    private const int UnmatchedConfirmationMs = 250;
    private const int MaxConsecutiveStalls = 3;

    private PendingSubmission? pending;

    internal event Action<SubmissionEvent>? Event;

    internal int Attempts { get; private set; }
    internal int Effects { get; private set; }
    internal int Unmatched { get; private set; }
    internal int Stalls { get; private set; }
    internal int ConsecutiveStalls { get; private set; }
    internal bool HasPending => pending != null;
    internal bool IsFaulted { get; private set; }
    internal string Status { get; private set; } = "等待提交";
    internal string LastResult { get; set; } = "尚未提交";
    internal Candidate? PendingTarget => pending?.Target;

    internal void Tick(
        long now,
        GameSnapshot snapshot,
        bool automatic,
        bool manualRequested,
        int intervalMs,
        bool protectionEnabled,
        string? safetyReason,
        ISubmissionGateway gateway)
    {
        cooldown.ProtectionEnabled = protectionEnabled;
        if (IsFaulted)
        {
            Status = "连续提交未生效，自动执行已暂停；请重新切换自动模式";
            return;
        }

        if (pending != null)
        {
            var resolution = Observe(now, snapshot);
            if (resolution == PendingResolution.Waiting)
            {
                if (safetyReason != null || now - pending.SubmittedAt < EffectTimeoutMs)
                {
                    Status = $"等待效果确认：{pending.Target.Word}";
                    return;
                }

                RecordStall(now, snapshot, intervalMs, protectionEnabled);
                return;
            }

            var completed = pending;
            pending = null;
            if (resolution == PendingResolution.Effected)
            {
                Effects++;
                ConsecutiveStalls = 0;
                schedule.Reset(now, intervalMs, protectionEnabled);
                Status = "已观察到提交效果";
                LastResult = $"已确认：{completed.Target.Word}\n{TargetPolicy.Reason(completed.Target)} · 分数 {snapshot.Score}";
                Raise(SubmissionEventKind.Effect, now, completed.Target, completed.Matched, snapshot, "观察到血量、词条、读条、目标消失或分数变化");
            }
            else
            {
                Unmatched++;
                schedule.Reset(now, intervalMs, protectionEnabled);
                Status = "词条未匹配，已重新选择";
                LastResult = $"未匹配：{completed.Target.Word}";
                Raise(SubmissionEventKind.Unmatched, now, completed.Target, completed.Matched, snapshot, "提交返回或观察结果为未匹配");
            }
        }

        if (safetyReason != null)
        {
            Status = safetyReason;
            return;
        }
        if (automatic && schedule.DueAt == long.MaxValue) schedule.Reset(now, intervalMs, protectionEnabled);
        if (!automatic && !manualRequested) return;
        if (!manualRequested && now < schedule.DueAt) return;
        if (cooldown.RemainingMs(now) > 0)
        {
            if (manualRequested) LastResult = "本次点击已取消：提交冷却尚未结束";
            return;
        }

        var target = TargetPolicy.Choose(snapshot.Candidates, Random.Shared);
        if (target == null)
        {
            Status = "当前没有可提交词条";
            if (manualRequested) LastResult = "未提交：当前没有可提交词条";
            else schedule.Reset(now, intervalMs, protectionEnabled);
            return;
        }
        if (!cooldown.TryAcquire(now)) return;

        Attempts++;
        bool matched;
        try
        {
            matched = gateway.Submit(target.Word);
        }
        finally
        {
            cooldown.Complete(now);
        }

        pending = new PendingSubmission(target, matched, now, snapshot.Score);
        Status = "提交已调用，等待效果确认";
        LastResult = $"{(manualRequested ? "手动" : "自动")}第 {Attempts} 次：{TargetPolicy.Reason(target)}\n词条：{target.Word}\n{(matched ? "词条匹配，等待效果" : "未匹配，等待确认")}";
        Raise(SubmissionEventKind.Attempt, now, target, matched, snapshot, "原生提交调用完成");
    }

    internal void Suspend()
    {
        pending = null;
        schedule.Clear();
        ConsecutiveStalls = 0;
        IsFaulted = false;
    }

    internal void ResetSchedule(long now, int intervalMs, bool protectionEnabled)
    {
        pending = null;
        IsFaulted = false;
        ConsecutiveStalls = 0;
        schedule.Reset(now, intervalMs, protectionEnabled);
    }

    internal void Reschedule(long now, int intervalMs, bool protectionEnabled) =>
        schedule.Reset(now, intervalMs, protectionEnabled);

    private PendingResolution Observe(long now, GameSnapshot snapshot)
    {
        if (pending == null) return PendingResolution.Waiting;
        var elapsed = now - pending.SubmittedAt;
        if (snapshot.Score > pending.BaselineScore) return PendingResolution.Effected;

        var target = snapshot.Candidates.FirstOrDefault(c => c.EntityId == pending.Target.EntityId);
        if (target == null)
        {
            if (!pending.Matched && elapsed >= UnmatchedConfirmationMs) return PendingResolution.Unmatched;
            pending.MissingSince ??= now;
            return pending.Matched && now - pending.MissingSince.Value >= DisappearConfirmationMs
                ? PendingResolution.Effected
                : PendingResolution.Waiting;
        }
        pending.MissingSince = null;
        if (!string.Equals(target.Word, pending.Target.Word, StringComparison.Ordinal)) return PendingResolution.Effected;
        if (target.HealthPercent < pending.Target.HealthPercent - 0.001f) return PendingResolution.Effected;
        if (target.IsDead && !pending.Target.IsDead) return PendingResolution.Effected;
        if (pending.Target.CastActionId != 0 && target.CastActionId == 0) return PendingResolution.Effected;
        if (pending.Target.CastProgress > 0 && target.CastProgress < pending.Target.CastProgress - 0.05f) return PendingResolution.Effected;
        if (!pending.Matched && elapsed >= UnmatchedConfirmationMs) return PendingResolution.Unmatched;
        return PendingResolution.Waiting;
    }

    private void RecordStall(long now, GameSnapshot snapshot, int intervalMs, bool protectionEnabled)
    {
        if (pending == null) return;
        var target = pending.Target;
        var matched = pending.Matched;
        pending = null;
        Stalls++;
        ConsecutiveStalls++;
        if (ConsecutiveStalls >= MaxConsecutiveStalls) IsFaulted = true;
        schedule.Reset(now, Math.Max(intervalMs, 2000), protectionEnabled);
        Status = IsFaulted ? "连续提交未生效，自动执行已暂停" : "提交未观察到效果，已退避";
        LastResult = $"未确认效果：{target.Word}\n连续未确认 {ConsecutiveStalls} 次";
        Raise(SubmissionEventKind.Stall, now, target, matched, snapshot, "超过效果确认时限");
        if (IsFaulted) Raise(SubmissionEventKind.Fault, now, target, matched, snapshot, "连续未确认达到上限");
    }

    private void Raise(SubmissionEventKind kind, long now, Candidate target, bool matched, GameSnapshot snapshot, string reason) =>
        Event?.Invoke(new SubmissionEvent(kind, now, target, matched, snapshot.Score, target.HealthPercent, reason));

    private enum PendingResolution
    {
        Waiting,
        Effected,
        Unmatched,
    }

    private sealed record PendingSubmission(Candidate Target, bool Matched, long SubmittedAt, uint BaselineScore)
    {
        internal long? MissingSince { get; set; }
    }
}
