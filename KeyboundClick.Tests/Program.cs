using System.Text.Json;
using KeyboundClick;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var random = new Random(20261028);
var normal = new Candidate(1, 19586, "普通敌人", "神罗", 0, false, CandidateRole.Normal, ScreenY: 300);
var boss = new Candidate(2, 19596, "Boss", "克劳德", 1, true, CandidateRole.Boss, ScreenY: 100);
var pot = new Candidate(3, 19592, "魔法罐", "给我圣灵药！", 4, false, CandidateRole.MagicPot, ScreenY: 200);
var barrier = new Candidate(9, 19824, "屏障", "守护", 2, false, CandidateRole.Barrier);
var casting = normal with { EntityId = 4, CastActionId = 40000, CastProgress = 0.8f };
Check(TargetPolicy.Choose([normal, boss, pot], random) == pot, "Magic pot must outrank ordinary enemies and the boss");
Check(TargetPolicy.Choose([normal, boss, pot, casting], random) == casting, "Urgent casts must outrank the magic pot");
Check(TargetPolicy.Choose([casting, pot, barrier], random) == barrier, "Barrier defense must outrank every combat target");
var nearer = normal with { EntityId = 5, ScreenY = 500, Word = "更长的词条" };
Check(TargetPolicy.Choose([normal, nearer], random) == nearer, "Enemies closer to the barrier must outrank distant enemies");
Check(TargetPolicy.Choose([], random) == null, "Empty input must not produce an action");
var gate = new ClickGate();
Check(!gate.Consume(), "No click must mean no submission");
Check(gate.Request() && !gate.Request(), "Duplicate pending clicks must be coalesced");
Check(gate.Consume() && !gate.Consume(), "One accepted click must be consumed only once");
gate.Request(); gate.Cancel(); Check(!gate.Consume(), "Cancelled clicks must never execute");
Check(EnemyKinds.IsBoss(19596, 1) && !EnemyKinds.IsBoss(19592, 4), "Boss must not be confused with the magic pot");
Check(!EnemyKinds.IsEnemy(19824, 2) && !EnemyKinds.IsEnemy(20146, 3), "Non-enemy objects must not be selected");
foreach (var baseline in new[] { 100, 1000, 1100, 1250, 3333, 10000 })
{
    var schedule = new IntervalSchedule(random);
    var unique = new HashSet<int>();
    for (var i = 0; i < 10000; i++)
    {
        var now = (long)i * 20000;
        schedule.Reset(now, baseline);
        var clamped = Math.Clamp(baseline, 1000, 10000);
        Check(schedule.LastDelayMs >= Math.Max(1000, Math.Ceiling(clamped * .8)) && schedule.LastDelayMs <= Math.Floor(clamped * 1.2), "Jitter outside capped ±20%");
        Check(!schedule.IsDue(now + schedule.LastDelayMs - 1) && schedule.IsDue(now + schedule.LastDelayMs), "Incorrect timer boundary");
        unique.Add(schedule.LastDelayMs);
    }
    Check(unique.Count > 10, "Interval must be rerandomized");
    schedule.Reset(100000000, baseline);
    Check(!schedule.IsDue(100000000), "Long stalls must not cause catch-up submissions");
}
var cooldown = new SubmissionCooldown(1000000);
Check(cooldown.TryAcquire(0), "First manual submission must be permitted");
Check(!cooldown.TryAcquire(999999), "Automatic submission cannot bypass a manual cooldown");
cooldown.Complete(250000); // Native call took 250 ms; cooldown is measured from completion.
Check(!cooldown.TryAcquire(1000000) && !cooldown.TryAcquire(1249999), "Slow native calls must not shorten the gap");
Check(cooldown.TryAcquire(1250000), "Submission must be allowed at exactly one second after completion");
Check(!cooldown.TryAcquire(1250000), "Same-frame manual/auto requests must produce only one submission");
cooldown.Complete(1250010); // Also the path used after a failed/throwing native call.
Check(!cooldown.TryAcquire(2250009) && cooldown.TryAcquire(2250010), "Failed submissions must also consume the cooldown");
Check(cooldown.RemainingMs(2250011) == 1000, "UI countdown must round up");
var optionalCooldown = new SubmissionCooldown(1000);
Check(optionalCooldown.ProtectionEnabled, "Protection must default to enabled");
optionalCooldown.Complete(100);
Check(!optionalCooldown.TryAcquire(200), "Existing protection must block early clicks");
optionalCooldown.ProtectionEnabled = false;
Check(optionalCooldown.RemainingMs(200) == 0 && optionalCooldown.TryAcquire(200), "Disabling must remove the remaining cooldown immediately");
optionalCooldown.Complete(250);
Check(optionalCooldown.TryAcquire(251), "Unprotected submissions must allow sub-second intervals");
optionalCooldown.Complete(300);
optionalCooldown.ProtectionEnabled = true;
Check(!optionalCooldown.TryAcquire(1299) && optionalCooldown.TryAcquire(1300), "Re-enabling must protect the latest actual completion");
foreach (var baseline in new[] { 0, 100, 500, 999, 1000, 10000 })
{
    var fastSchedule = new IntervalSchedule(random);
    for (var j = 0; j < 1000; j++)
    {
        fastSchedule.Reset(10000, baseline, false);
        var clamped = Math.Clamp(baseline, 100, 10000);
        Check(fastSchedule.LastDelayMs >= Math.Ceiling(clamped * .8) && fastSchedule.LastDelayMs <= Math.Floor(clamped * 1.2), "Unprotected jitter must retain full ±20% range");
        Check(!fastSchedule.IsDue(10000), "Unprotected schedule must not burst in the same frame");
    }
    fastSchedule.Reset(10000, baseline, true);
    Check(fastSchedule.LastDelayMs >= 1000, "Re-enabled protection must restore schedule floor");
}
var fastShared = new SubmissionCooldown(1000) { ProtectionEnabled = false };
var fastKeys = new FakeKeyboard();
var fastError = new AntiIdleSubmit(fastKeys, fastShared);
fastError.Update(0, 0, true, "", 7);
fastShared.Complete(19999);
fastError.Update(20000, 20000, true, "", 7);
fastError.Update(20050, 20050, true, "1", 7);
fastError.Update(20051, 20051, true, "1", 7);
fastError.Update(20052, 20052, true, "1", 7);
Check(fastKeys.Held == 0x0D, "Anti-idle must honor the shared protection switch");
fastError.Update(20102, 20102, true, "", 7);
fastError.Update(20103, 20103, true, "", 7);
Check(fastError.Completed == 1 && fastShared.TryAcquire(20104), "Normal submissions must not retain a hidden cooldown after unprotected anti-idle");
var window = new DutyWindowState();
window.Observe(false);
Check(!window.IsOpen, "Startup outside duty must stay hidden");
window.SetOpen(true);
Check(window.IsOpen && !window.CanExecute, "Settings must open outside the duty without enabling execution");
window.Observe(false);
Check(window.IsOpen && !window.CanExecute, "Outside-duty frames must not close manually opened settings");
window.Observe(true);
Check(window.IsOpen && window.CanExecute, "Entering the duty must open the window and permit execution");
window.SetOpen(false);
window.Observe(true);
Check(!window.IsOpen, "Manual close must survive subsequent frames in the same duty");
window.SetOpen(true);
window.Observe(false);
Check(!window.IsOpen && !window.CanExecute, "Leaving the duty must hide the window and disallow execution");
window.SetOpen(true);
window.Observe(false);
Check(window.IsOpen && !window.CanExecute, "Settings must be manually reopenable after exiting the duty");
window.Observe(true);
Check(window.IsOpen, "A later duty entry must open the window again");
var reloadWindow = new DutyWindowState();
reloadWindow.Observe(true);
Check(!reloadWindow.IsOpen, "Reloading inside the duty must not pop up a window at startup");

var keyboard = new FakeKeyboard();
var pulse = new AntiIdlePulse(keyboard);
pulse.Update(0, true);
pulse.Update(19_999, true);
Check(keyboard.Events.Count == 0, "No activity keys before the 20 second interval");
pulse.Update(20_000, true);
Check(pulse.IsBusy && keyboard.Held == 0x7C, "First pulse must press F13");
pulse.Update(20_049, true);
Check(keyboard.Held == 0x7C, "Keys must remain held for at least 50 ms across frames");
for (var i = 0; i < 6; i++)
{
    if (i > 0) pulse.Update(20_000 + i * 100, true);
    pulse.Update(20_050 + i * 100, true);
}
Check(pulse.Completed == 1 && !pulse.IsBusy && keyboard.Held == 0, "A full pulse must leave every key released");
Check(keyboard.Events.Where(e => e.Down).Select(e => e.Key).SequenceEqual(new ushort[] { 0x7C, 0x7D, 0xA0, 0xA1, 0x25, 0x27 }), "Activity pulse must use only the documented six keys, never Enter");
pulse.Update(40_549, true);
Check(keyboard.Events.Count == 12, "Next pulse must be 20 seconds after completion");
pulse.Update(40_550, true);
keyboard.Foreground = false;
pulse.Update(40_551, true);
Check(!pulse.IsBusy && keyboard.Held == 0 && keyboard.Events.Count == 14, "Focus loss must release the owned key without sending another key");
pulse.Update(100_000, true);
Check(keyboard.Events.Count == 14, "No keys may be pressed in another application");
keyboard.Foreground = true;
keyboard.UserTyping = true;
pulse.Update(100_001, true);
Check(keyboard.Events.Count == 14, "Physical user input must suppress activity keys");
keyboard.UserTyping = false;
pulse.Update(100_002, true);
pulse.Update(120_002, true);
pulse.Update(120_003, false);
Check(!pulse.IsBusy && keyboard.Held == 0, "Stopping, closing, dialogs or input locks must release the current key");
var eventsBefore = keyboard.Events.Count;
pulse.Update(500_000, true);
Check(keyboard.Events.Count == eventsBefore, "Resuming must not immediately catch up missed pulses");
pulse.Update(520_000, true);
keyboard.FailRelease = true;
pulse.Cancel(520_001);
Check(pulse.IsBusy && keyboard.Held != 0, "A failed key release must remain tracked");
keyboard.FailRelease = false;
pulse.Update(520_002, false);
Check(!pulse.IsBusy && keyboard.Held == 0, "Cancellation must retry a failed release");
keyboard.FailPress = true;
pulse.Update(530_000, true);
pulse.Update(550_000, true);
Check(!pulse.IsBusy && keyboard.Held == 0, "A failed key press must not create a stuck-key state");
var shared = new SubmissionCooldown(1000);
var numberKeyboard = new FakeKeyboard();
var wrongAnswer = new AntiIdleSubmit(numberKeyboard, shared);
wrongAnswer.Update(0, 0, true, "", 7);
Check(shared.TryAcquire(19_500), "Normal submission setup must succeed");
shared.Complete(19_500);
wrongAnswer.Update(20_000, 20_000, true, "", 7);
wrongAnswer.Update(20_050, 20_050, true, "1", 7);
wrongAnswer.Update(20_100, 20_100, true, "1", 7);
wrongAnswer.Update(20_499, 20_499, true, "1", 7);
Check(!numberKeyboard.Events.Any(e => e is { Key: 0x0D, Down: true }), "Error submission must respect the preceding normal submission's cooldown");
wrongAnswer.Update(20_500, 20_500, true, "1", 7);
Check(numberKeyboard.Held == 0x0D, "Enter is allowed only once the cooldown expires and 1 is observed");
wrongAnswer.Update(20_550, 20_550, true, "", 7);
wrongAnswer.Update(20_551, 20_551, true, "", 7);
Check(wrongAnswer.Completed == 1 && !wrongAnswer.IsBusy && numberKeyboard.Held == 0, "Error submission completes only after input clears and Enter is released");
Check(!shared.TryAcquire(21_550) && shared.TryAcquire(21_551), "Error submission must reserve a full second before the next normal/manual submission");

foreach (var interruption in new[] { "different text", "session", "foreground", "disabled", "timeout" })
{
    var backend = new FakeKeyboard();
    var attempt = new AntiIdleSubmit(backend, new SubmissionCooldown(1000));
    attempt.Update(0, 0, true, "", 7);
    attempt.Update(20_000, 20_000, true, "", 7);
    if (interruption == "foreground") backend.Foreground = false;
    attempt.Update(interruption == "timeout" ? 22_001 : 20_100, 22_001,
        interruption != "disabled", interruption == "different text" ? "玩家自己的文字" : "",
        interruption == "session" ? 8 : 7);
    Check(!attempt.IsBusy && backend.Held == 0, "Interruption must release digit key: " + interruption);
    Check(!backend.Events.Any(e => e is { Key: 0x0D, Down: true }), "Never press Enter without confirmed owned text: " + interruption);
}
var gapKeys = new FakeKeyboard();
var gapPulse = new AntiIdlePulse(gapKeys);
gapPulse.Update(0, true);
gapPulse.Update(10_000, false);
gapPulse.Update(19_999, true);
gapPulse.Update(20_000, true);
Check(gapPulse.IsBusy && gapKeys.Held == 0x7C, "Enemy/wave gaps must not restart the function-key timer");
gapPulse.Cancel(20_001);
var gapSubmitKeys = new FakeKeyboard();
var gapSubmit = new AntiIdleSubmit(gapSubmitKeys, new SubmissionCooldown(1000));
gapSubmit.Update(0, 0, true, "", 7);
gapSubmit.Update(10_000, 10_000, false, null, 0);
gapSubmit.Update(19_999, 19_999, true, "", 7);
gapSubmit.Update(20_000, 20_000, true, "", 7);
Check(gapSubmit.IsBusy && gapSubmitKeys.Held == 0x31, "Enemy/wave gaps must not restart the 1 + Enter timer");
gapSubmit.Cancel(20_001, 20_001);
Check(ConfirmationPolicy.CanCommence(true, 2, 1095, "出发"), "Keybound popped duty must be accepted");
Check(!ConfirmationPolicy.CanCommence(true, 2, 999, "出发"), "Other duties must never be accepted");
Check(!ConfirmationPolicy.CanCommence(true, 1, 1095, "出发"), "Roulette IDs must not be mistaken for duty IDs");
Check(!ConfirmationPolicy.CanCommence(false, 2, 1095, "出发"), "Disabled commencement must not run");
Check(!ConfirmationPolicy.CanCommence(true, 2, 1095, "取消"), "Unexpected button must not be clicked");
Check(ConfirmationPolicy.CanExit(true, true, "退出"), "Keybound result exit must be allowed");
Check(!ConfirmationPolicy.CanExit(true, false, "退出") && !ConfirmationPolicy.CanExit(false, true, "退出"), "Exit requires both duty and setting");
Check(!ConfirmationPolicy.CanExit(true, true, "是"), "Generic yes/no dialogs must not be confirmed");
var confirmation = new ConfirmationGate();
Check(!confirmation.TryAcquire(0, 123, true) && !confirmation.TryAcquire(999, 123, true), "Wait for a stable popup");
Check(confirmation.TryAcquire(1000, 123, true), "Click at one second");
Check(!confirmation.TryAcquire(100000, 123, true), "A persistent popup must only be clicked once");
confirmation.TryAcquire(100001, 123, false);
Check(!confirmation.TryAcquire(102000, 123, true), "Toggling must not double click the same popup");
confirmation.TryAcquire(103000, 0, false);
Check(!confirmation.TryAcquire(104000, 123, true), "Reused addon address must wait again");
confirmation.TryAcquire(104900, 123, false);
Check(!confirmation.TryAcquire(105000, 123, true) && !confirmation.TryAcquire(105999, 123, true), "Unavailable buttons reset stable time");
Check(confirmation.TryAcquire(106000, 123, true), "New popup may be confirmed");
Check(!confirmation.TryAcquire(107000, 456, true) && confirmation.TryAcquire(108000, 456, true), "Different popup starts a new cycle");
static GameSnapshot TestSnapshot(Candidate[] candidates, uint score = 0, bool allowed = true, int lockSeconds = 0, CastSnapshot[]? haltCasts = null) =>
    new(2, score, 0, false, allowed, lockSeconds, allowed ? "Ready" : "Locked", "", 1, 0, candidates, haltCasts ?? []);

static void Tick(SubmissionCoordinator coordinator, long now, GameSnapshot snapshot, ISubmissionGateway gateway, bool manual = true, string? safety = null) =>
    coordinator.Tick(now, snapshot, false, manual, 1000, true, safety, gateway);

var gateway = new FakeGateway();
var coordinated = new SubmissionCoordinator(new SubmissionCooldown(1000), new IntervalSchedule(random));
Tick(coordinated, 0, TestSnapshot([normal]), gateway);
Check(coordinated.Attempts == 1 && coordinated.HasPending && gateway.Words.Count == 1, "A native call must enter the pending-effect state");
Tick(coordinated, 100, TestSnapshot([normal]), gateway);
Check(coordinated.Attempts == 1 && coordinated.Effects == 0, "An unchanged snapshot must not be counted as success");
Tick(coordinated, 100, TestSnapshot([normal with { HealthPercent = 90 }]), gateway);
Check(coordinated.Effects == 1 && !coordinated.HasPending, "A health decrease must complete the submission loop");
Tick(coordinated, 1000, TestSnapshot([normal]), gateway);
Tick(coordinated, 2100, TestSnapshot([normal]), gateway);
Check(coordinated.Stalls == 1 && coordinated.ConsecutiveStalls == 1, "Matched but unchanged must become a stall, not a success");
Tick(coordinated, 3000, TestSnapshot([normal]), gateway);
Tick(coordinated, 4000, TestSnapshot([normal]), gateway);
Tick(coordinated, 6000, TestSnapshot([normal]), gateway);
Tick(coordinated, 7000, TestSnapshot([normal]), gateway);
Check(coordinated.Stalls == 3 && coordinated.IsFaulted && coordinated.Attempts == 4, "Repeated ineffective submissions must trip the safety breaker");
Tick(coordinated, 8000, TestSnapshot([normal]), gateway);
Check(coordinated.Attempts == 4 && gateway.Words.Count == 4, "A faulted coordinator must never keep firing submissions");

var unmatchedGateway = new FakeGateway { Result = false };
var unmatchedCoordinator = new SubmissionCoordinator(new SubmissionCooldown(1000), new IntervalSchedule(random));
Tick(unmatchedCoordinator, 0, TestSnapshot([normal]), unmatchedGateway);
Tick(unmatchedCoordinator, 250, TestSnapshot([normal]), unmatchedGateway);
Check(unmatchedCoordinator.Unmatched == 1 && unmatchedCoordinator.Stalls == 0 && !unmatchedCoordinator.HasPending,
    "An explicit unmatched result must resolve without being reported as a hit");
var scoreCoordinator = new SubmissionCoordinator(new SubmissionCooldown(1000), new IntervalSchedule(random));
var scoreGateway = new FakeGateway();
Tick(scoreCoordinator, 0, TestSnapshot([normal], 100), scoreGateway);
Tick(scoreCoordinator, 50, TestSnapshot([normal], 125), scoreGateway);
Check(scoreCoordinator.Effects == 1, "A score increase must confirm an effect even if health does not change");
var blockedCoordinator = new SubmissionCoordinator(new SubmissionCooldown(1000), new IntervalSchedule(random));
Tick(blockedCoordinator, 0, TestSnapshot([normal]), new FakeGateway(), safety: "停止输入");
Check(blockedCoordinator.Attempts == 0, "The safety gate must have absolute veto over submission");
var automaticCoordinator = new SubmissionCoordinator(new SubmissionCooldown(1000), new IntervalSchedule(random));
var automaticGateway = new FakeGateway();
automaticCoordinator.Tick(0, TestSnapshot([normal]), true, false, 1000, true, null, automaticGateway);
Check(automaticCoordinator.Attempts == 0, "Automatic mode must arm its initial delay");
automaticCoordinator.Tick(1300, TestSnapshot([normal]), true, false, 1000, true, null, automaticGateway);
Check(automaticCoordinator.Attempts == 1, "Automatic mode must fire once the initial delay expires");

var safetyGate = new SafetyGate();
var halt = new CastSnapshot(1, 49529, "停止输入技能", 4, 5);
Check(safetyGate.Evaluate(0, TestSnapshot([normal], haltCasts: [halt])) != null, "A landing halt cast must block input");
Check(safetyGate.Evaluate(100, TestSnapshot([normal])) != null, "The post-cast hold must start after the cast disappears");
Check(safetyGate.Evaluate(3599, TestSnapshot([normal])) != null, "The post-cast hold must not end early");
Check(safetyGate.Evaluate(3600, TestSnapshot([normal])) == null, "The post-cast hold must expire at the configured boundary");
var earlySafety = new SafetyGate();
Check(earlySafety.Evaluate(0, TestSnapshot([normal], haltCasts: [halt with { CurrentSeconds = 1 }])) == null,
    "Early cast progress must still permit useful attacks");
Check(earlySafety.Evaluate(100, TestSnapshot([normal])) == null, "An interrupted early cast must not cause a post-cast hold");
Check(new SafetyGate().Evaluate(0, TestSnapshot([normal], lockSeconds: 2)) != null, "The game's own input lock must always win");
var customSafety = new SafetyGate();
var customHalt = halt with { CurrentSeconds = 3.2f };
Check(customSafety.Evaluate(0, TestSnapshot([normal], haltCasts: [customHalt]), 1000, 1000) == null,
    "A shorter pre-cast threshold must not block while more time remains");
Check(customSafety.Evaluate(100, TestSnapshot([normal], haltCasts: [customHalt with { CurrentSeconds = 4.3f }]), 1000, 1000) != null,
    "A shorter pre-cast threshold must block when its remaining-time boundary is crossed");
Check(customSafety.Evaluate(200, TestSnapshot([normal]), 1000, 1000) != null, "A custom post-cast hold must start after the cast disappears");
Check(customSafety.Evaluate(1100, TestSnapshot([normal]), 1000, 5000) != null, "Extending the post-cast hold must apply to the active halt window");
Check(customSafety.Evaluate(1100, TestSnapshot([normal]), 1000, 1000) != null, "The custom post-cast hold must remain before its boundary");
Check(customSafety.Evaluate(1200, TestSnapshot([normal]), 1000, 1000) == null, "Shortening the active hold must allow resuming at its new boundary");
Check(customSafety.Evaluate(1200, TestSnapshot([normal], lockSeconds: 2), 1000, 1000) != null, "Input lock must still win after a custom hold ends");

var disappearanceCoordinator = new SubmissionCoordinator(new SubmissionCooldown(1000), new IntervalSchedule(random));
var disappearanceGateway = new FakeGateway();
Tick(disappearanceCoordinator, 0, TestSnapshot([normal]), disappearanceGateway);
Tick(disappearanceCoordinator, 50, TestSnapshot([]), disappearanceGateway);
Check(disappearanceCoordinator.Effects == 0 && disappearanceCoordinator.HasPending, "A single missing snapshot must not confirm success");
Tick(disappearanceCoordinator, 150, TestSnapshot([]), disappearanceGateway);
Check(disappearanceCoordinator.Effects == 1 && !disappearanceCoordinator.HasPending, "A sustained disappearance must resolve the pending submission");

var journalTestRoot = Path.Combine(AppContext.BaseDirectory, "journal-test");
if (Directory.Exists(journalTestRoot)) Directory.Delete(journalTestRoot, true);
var journal = new DiagnosticJournal(journalTestRoot);
journal.Record("self-test", new { Value = 42, Text = "词条" });
var journalPath = journal.FilePath;
journal.Dispose();
Check(journal.Error == null && File.Exists(journalPath), "The diagnostic journal must create and close its JSONL file cleanly");
using var journalJson = JsonDocument.Parse(File.ReadAllText(journalPath));
var journalData = journalJson.RootElement.GetProperty("Data");
Check(journalJson.RootElement.GetProperty("Kind").GetString() == "self-test" &&
      journalData.GetProperty("Value").GetInt32() == 42 && journalData.GetProperty("Text").GetString() == "词条",
    "The diagnostic journal must persist structured event data");
Directory.Delete(journalTestRoot, true);

Console.WriteLine("PASS: high-score targets, closed-loop effects, stall breaker, safety gates, shared cooldowns, activity cleanup, confirmations and JSONL diagnostics.");

sealed class FakeGateway : ISubmissionGateway
{
    public bool Result { get; set; } = true;
    public List<string> Words { get; } = [];
    public bool Submit(string word)
    {
        Words.Add(word);
        return Result;
    }
}

sealed class FakeKeyboard : IActivityKeyboard
{
    public bool Foreground = true;
    public bool UserTyping;
    public bool FailPress;
    public bool FailRelease;
    public ushort Held;
    public readonly List<(ushort Key, bool Down)> Events = [];
    public bool IsGameForeground => Foreground;
    public bool HasUserInput(ushort ownedKey) => UserTyping;
    public bool Press(ushort key)
    {
        if (FailPress || !Foreground) return false;
        if (Held != 0) throw new Exception("Two activity keys were held simultaneously");
        Held = key;
        Events.Add((key, true));
        return true;
    }
    public bool Release(ushort key)
    {
        if (FailRelease) return false;
        if (Held != key) throw new Exception("Attempted to release an unowned key");
        Held = 0;
        Events.Add((key, false));
        return true;
    }
}
