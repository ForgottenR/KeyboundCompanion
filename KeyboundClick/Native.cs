using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.STD;

namespace KeyboundClick;

// Layouts adapted from AtmoOmen/FFXIVClientStructs.Expansion at 22b9e96bca56aab9d18a60956dfb71c14d3c50c2 (MIT).
// Never retain pointers or StdMap entries across framework ticks.

[StructLayout(LayoutKind.Explicit, Size = 752)]
internal unsafe struct KtgHud
{
    [FieldOffset(40)] internal StdMap<uint, KtgEntity> Entities;
    [FieldOffset(608)] internal uint Score;
    [FieldOffset(612)] internal uint TodoValue;
    [FieldOffset(616)] internal uint ScoreStage;
    [FieldOffset(620)] internal uint ScoreBonusTime;
    [FieldOffset(624)] internal int Stage;
    [FieldOffset(640)] internal uint InputAddonId;
    [FieldOffset(672)] internal byte IsFever;
    [FieldOffset(673)] internal byte IsShown;
    [FieldOffset(674)] internal byte HasDirector;
}

[StructLayout(LayoutKind.Explicit, Size = 376)]
internal struct KtgEntity
{
    [FieldOffset(0)] internal uint EntityId;
    [FieldOffset(4)] internal uint Kind;
    [FieldOffset(8)] internal Utf8String Name;
    [FieldOffset(112)] internal Utf8String Title;
    [FieldOffset(220)] internal float X;
    [FieldOffset(224)] internal float Y;
    [FieldOffset(236)] internal byte IsVisible;
    [FieldOffset(244)] internal float HealthPercent;
    [FieldOffset(248)] internal float CastProgress;
    [FieldOffset(256)] internal Utf8String CastActionName;
    [FieldOffset(360)] internal byte IsImportantCast;
    [FieldOffset(362)] internal short CastParam;
    [FieldOffset(364)] internal float Timestamp;
    [FieldOffset(368)] internal byte Disposed;
}

[StructLayout(LayoutKind.Explicit, Size = 96)]
internal unsafe struct KtgInput
{
    [FieldOffset(40)] internal KtgHud* Hud;
    [FieldOffset(80)] internal float IdleSeconds;
    [FieldOffset(84)] internal float KeyHoldSeconds;
    [FieldOffset(88)] internal ushort HeldKey;
    [FieldOffset(90)] internal ushort HeldKeyFrames;
}
