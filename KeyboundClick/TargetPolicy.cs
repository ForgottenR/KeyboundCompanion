namespace KeyboundClick;

internal static class TargetPolicy
{
    internal static Candidate? Choose(IReadOnlyList<Candidate> candidates, Random random)
    {
        Candidate? best = null;
        var ties = 0;
        foreach (var candidate in candidates)
        {
            if (candidate.Role == CandidateRole.Unknown) continue;
            var comparison = best == null ? -1 : Compare(candidate, best);
            if (comparison < 0)
            {
                best = candidate;
                ties = 1;
            }
            else if (comparison == 0 && random.Next(++ties) == 0)
            {
                best = candidate;
            }
        }
        return best;
    }

    internal static string Reason(Candidate candidate) => Rank(candidate) switch
    {
        0 => "屏障防御",
        1 => "紧急读条",
        2 => "魔法罐增益",
        3 => "近敌优先",
        4 => "首领输出",
        _ => "死亡词条补分",
    };

    private static int Rank(Candidate candidate)
    {
        if (candidate.Role == CandidateRole.Barrier) return 0;
        if (candidate.IsDead) return 5;
        if (candidate.IsCasting) return 1;
        return candidate.Role switch
        {
            CandidateRole.MagicPot => 2,
            CandidateRole.Normal => 3,
            CandidateRole.Boss => 4,
            _ => 6,
        };
    }

    private static int Compare(Candidate left, Candidate right)
    {
        var rank = Rank(left).CompareTo(Rank(right));
        if (rank != 0) return rank;
        if (Rank(left) == 1)
        {
            var cast = right.CastProgress.CompareTo(left.CastProgress);
            if (cast != 0) return cast;
        }
        var distance = right.ScreenY.CompareTo(left.ScreenY);
        if (distance != 0) return distance;
        var length = left.Word.Length.CompareTo(right.Word.Length);
        if (length != 0) return length;
        var health = left.HealthPercent.CompareTo(right.HealthPercent);
        return health != 0 ? health : left.EntityId.CompareTo(right.EntityId);
    }
}
