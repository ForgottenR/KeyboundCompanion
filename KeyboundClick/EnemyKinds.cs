namespace KeyboundClick;

internal static class EnemyKinds
{
    // CN KTG identities verified against CollectEntities and KTGBNpc. Kinds 2/3 are defensive objects.
    internal static CandidateRole GetRole(uint dataId, uint kind) => kind switch
    {
        0 or 1 when kind == 1 && dataId == 19596 => CandidateRole.Boss,
        0 or 1 => CandidateRole.Normal,
        2 or 3 => CandidateRole.Barrier,
        4 => CandidateRole.MagicPot,
        _ => CandidateRole.Unknown,
    };

    internal static bool IsEnemy(uint dataId, uint kind)
    {
        var role = GetRole(dataId, kind);
        return role is CandidateRole.Normal or CandidateRole.Boss or CandidateRole.MagicPot;
    }

    internal static bool IsBoss(uint dataId, uint kind) => GetRole(dataId, kind) == CandidateRole.Boss;
}
