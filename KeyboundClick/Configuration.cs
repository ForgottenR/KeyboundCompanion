using Dalamud.Configuration;

namespace KeyboundClick;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;
    public bool Automatic { get; set; }
    public int IntervalMs { get; set; } = 1000;
    public bool AntiIdleEnabled { get; set; }
    public int AntiIdleMode { get; set; } = 1;
    public bool AutoCommence = true;
    public bool AutoExitResults = true;
    public bool SubmissionProtection = true;
    public int HaltPreCastMs { get; set; } = 1800;
    public int HaltPostCastMs { get; set; } = 3500;

    public void Normalize()
    {
        if (Version < 2)
        {
            AntiIdleMode = 1;
            Version = 2;
        }
        if (Version < 3)
        {
            HaltPreCastMs = 1800;
            HaltPostCastMs = 3500;
            Version = 3;
        }
        IntervalMs = Math.Clamp(IntervalMs, SubmissionProtection ? 1000 : 100, 10000);
        AntiIdleMode = Math.Clamp(AntiIdleMode, 0, 1);
        HaltPreCastMs = Math.Clamp(HaltPreCastMs, 0, 5000);
        HaltPostCastMs = Math.Clamp(HaltPostCastMs, 500, 10000);
    }
}
