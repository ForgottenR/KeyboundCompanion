using System.Numerics;
using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace KeyboundClick;

public sealed unsafe partial class Plugin : IDalamudPlugin
{
    private readonly IDalamudPluginInterface Pi;
    private readonly ICommandManager Commands;
    private readonly IFramework Framework;
    private readonly IClientState Client;
    private readonly IGameGui Gui;
    private readonly IPluginLog Log;

    private readonly ClickGate gate = new();
    private readonly SubmissionCooldown cooldown = new(1000);
    private readonly IntervalSchedule schedule = new(Random.Shared);
    private readonly SubmissionCoordinator coordinator;
    private readonly GameStateReader stateReader;
    private readonly NativeSubmitter submitter;
    private readonly SafetyGate safety = new();
    private readonly DutyWindowState window = new();
    private readonly AntiIdlePulse antiIdle = new(new WindowsActivityKeyboard());
    private readonly AntiIdleSubmit antiIdleSubmit;
    private readonly Configuration configuration;
    private readonly DiagnosticJournal? journal;

    private GameSnapshot? snapshot;
    private string status = "等待进入魔光键影";
    private string? currentSafetyReason;
    private string? lastLoggedSafetyReason;
    private long saveSettingsAt;
    private long refreshedAt;
    private bool pausedByError;
    private bool disposed;
    private uint requestedTerritory;
    private long requestedAt;
    private bool automatic;
    private int intervalMs = 1000;
    private bool antiIdleEnabled = true;
    private int antiIdleMode;

    private bool open
    {
        get => window.IsOpen;
        set => window.SetOpen(value);
    }

    public Plugin(
        IDalamudPluginInterface pi,
        ICommandManager commands,
        IFramework framework,
        IObjectTable objects,
        IClientState client,
        IGameGui gui,
        ISigScanner scanner,
        IDataManager data,
        IPluginLog log)
    {
        Pi = pi;
        Commands = commands;
        Framework = framework;
        Client = client;
        Gui = gui;
        Log = log;
        stateReader = new GameStateReader(client, objects, gui, data);
        submitter = new NativeSubmitter(scanner, log);
        coordinator = new SubmissionCoordinator(cooldown, schedule);
        coordinator.Event += OnSubmissionEvent;
        antiIdleSubmit = new AntiIdleSubmit(new WindowsActivityKeyboard(), cooldown);

        try { configuration = Pi.GetPluginConfig() as Configuration ?? new Configuration(); }
        catch (Exception ex)
        {
            configuration = new Configuration();
            Log.Warning(ex, "Could not load KeyboundClick settings; using defaults");
        }
        configuration.Normalize();
        automatic = configuration.Automatic;
        intervalMs = configuration.IntervalMs;
        antiIdleEnabled = configuration.AntiIdleEnabled;
        antiIdleMode = configuration.AntiIdleMode;
        cooldown.ProtectionEnabled = configuration.SubmissionProtection;

        try
        {
            journal = new DiagnosticJournal(Pi.GetPluginConfigDirectory());
            journal.Record("session-start", new
            {
                PluginVersion = typeof(Plugin).Assembly.GetName().Version?.ToString(),
                SubmitAvailable = submitter.IsAvailable,
                submitter.CheckedSignatureCount,
                submitter.InitializationError,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not initialize the diagnostic journal");
        }

        Commands.AddHandler("/ktgclick", new CommandInfo((_, _) => open = !open) { HelpMessage = "打开或关闭键影随行窗口" });
        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenMainUi += Open;
        Pi.UiBuilder.OpenConfigUi += Open;
        Framework.Update += Update;
        Log.Information("KeyboundClick loaded; signature resolved: {Resolved}", submitter.IsAvailable);
    }

    private void Open() => open = true;

    private void Update(IFramework _)
    {
        if (disposed) return;
        var now = Environment.TickCount64;
        try
        {
            if (saveSettingsAt != 0 && now >= saveSettingsAt) SaveSettings();
            window.Observe(stateReader.IsInDuty());
            if (UpdateConfirmations(now)) return;
            if (!window.CanExecute || pausedByError)
            {
                snapshot = null;
                if (!window.InDuty)
                {
                    pausedByError = false;
                    status = "当前不在魔光键影，可提前调整设置。";
                }
                gate.Cancel();
                CancelActivity();
                coordinator.Suspend();
                safety.Reset();
                return;
            }

            if (submitter.InitializationError != null)
            {
                status = submitter.InitializationError;
                CancelActivity();
                return;
            }

            if (snapshot == null || now - refreshedAt >= 100)
            {
                snapshot = stateReader.Read(out status);
                refreshedAt = now;
            }
            if (snapshot == null)
            {
                CancelActivity();
                coordinator.Suspend();
                return;
            }

            currentSafetyReason = safety.Evaluate(now, snapshot, configuration.HaltPreCastMs, configuration.HaltPostCastMs);
            if (currentSafetyReason != lastLoggedSafetyReason)
            {
                journal?.Record("safety", new { Now = now, Reason = currentSafetyReason, snapshot.Stage, snapshot.Score, snapshot.IdleSeconds, HaltCasts = snapshot.HaltCasts, configuration.HaltPreCastMs, configuration.HaltPostCastMs });
                lastLoggedSafetyReason = currentSafetyReason;
            }
            var clicked = gate.Consume();
            if (clicked && (Client.TerritoryType != requestedTerritory || now - requestedAt > 1500))
            {
                clicked = false;
                coordinator.LastResult = "本次点击已取消：场景发生变化";
            }

            var needsActivity = snapshot.IdleSeconds >= 5 || antiIdle.IsBusy || antiIdleSubmit.IsBusy;
            var allowActivity = needsActivity && antiIdleEnabled && automatic && currentSafetyReason == null && !coordinator.HasPending && !clicked;
            if (antiIdleMode == 0)
            {
                antiIdle.Cancel(now);
                if (clicked && !antiIdleSubmit.IsBusy) antiIdleSubmit.Cancel(now, now);
                else antiIdleSubmit.Update(now, now, allowActivity, snapshot.InputText, snapshot.InputSession);
            }
            else
            {
                antiIdleSubmit.Cancel(now, now);
                if (clicked) antiIdle.Cancel(now);
                else antiIdle.Update(now, allowActivity && snapshot.InputText.Length == 0);
            }
            if (antiIdle.IsBusy || antiIdleSubmit.IsBusy)
            {
                if (clicked) coordinator.LastResult = "防闲置输入中，本次点击已取消，请稍后重试";
                return;
            }

            coordinator.Tick(now, snapshot, automatic, clicked, intervalMs, configuration.SubmissionProtection, currentSafetyReason, submitter);
            status = coordinator.Status;
        }
        catch (Exception ex)
        {
            pausedByError = true;
            CancelActivity();
            coordinator.Suspend();
            status = "读取或提交失败，执行已暂停";
            coordinator.LastResult = "本次操作已结束；请重新切换自动模式";
            Log.Error(ex, "KeyboundClick read/submit failed; no retry scheduled");
            journal?.Record("execution-error", new { Now = now, Exception = ex.ToString() });
        }
    }

    private void OnSubmissionEvent(SubmissionEvent e)
    {
        journal?.Record("submission", e);
        if (e.Kind is SubmissionEventKind.Stall or SubmissionEventKind.Fault)
            Log.Warning("Submission {Kind}: entity={Entity:X}, word={Word}, matched={Matched}, score={Score}", e.Kind, e.Target?.EntityId ?? 0, e.Target?.Word ?? string.Empty, e.Matched, e.Score);
        else
            Log.Information("Submission {Kind}: entity={Entity:X}, word={Word}, matched={Matched}, score={Score}", e.Kind, e.Target?.EntityId ?? 0, e.Target?.Word ?? string.Empty, e.Matched, e.Score);
    }

    private void Draw()
    {
        if (!open) { PauseExecution(); return; }
        ImGui.SetNextWindowSize(new Vector2(500, 500), ImGuiCond.FirstUseEver);
        var visible = open;
        if (ImGui.Begin("键影随行###KeyboundClick", ref visible))
        {
            ImGui.TextUnformatted("高分策略：屏障 > 紧急读条 > 魔法罐 > 近敌 > 首领");
            ImGui.TextWrapped(status);
            if (snapshot != null)
                ImGui.TextDisabled($"分数 {snapshot.Score} · 阶段 {snapshot.Stage} · 空闲 {snapshot.IdleSeconds:F1} 秒 · {(snapshot.IsFever ? "爆发中" : "未爆发")}");
            ImGui.TextDisabled($"尝试 {coordinator.Attempts} · 确认 {coordinator.Effects} · 未匹配 {coordinator.Unmatched} · 未确认 {coordinator.Stalls}");
            ImGui.Spacing();

            var remainingMs = cooldown.RemainingMs(Environment.TickCount64);
            ImGui.BeginDisabled(!window.InDuty || snapshot == null || !snapshot.InputAllowed || snapshot.InputLockSeconds > 0 ||
                                currentSafetyReason != null || snapshot.Candidates.Length == 0 || gate.IsPending ||
                                coordinator.HasPending || remainingMs > 0 || antiIdle.IsBusy || antiIdleSubmit.IsBusy);
            if (ImGui.Button("提交一次", new Vector2(-1, 42)))
            {
                requestedTerritory = Client.TerritoryType;
                requestedAt = Environment.TickCount64;
                gate.Request();
            }
            ImGui.EndDisabled();
            if (coordinator.HasPending) ImGui.TextDisabled("正在等待游戏状态证明本次提交已生效。");
            else if (remainingMs > 0) ImGui.TextDisabled($"冷却中：剩余 {remainingMs / 1000f:F1} 秒");

            if (ImGui.Checkbox("1 秒提交保护", ref configuration.SubmissionProtection))
            {
                intervalMs = Math.Clamp(intervalMs, configuration.SubmissionProtection ? 1000 : 100, 10000);
                coordinator.ResetSchedule(Environment.TickCount64, intervalMs, configuration.SubmissionProtection);
                SaveSettings();
            }
            if (ImGui.Checkbox("自动高分提交", ref automatic))
            {
                pausedByError = false;
                coordinator.ResetSchedule(Environment.TickCount64, intervalMs, configuration.SubmissionProtection);
                if (!automatic) CancelActivity();
                SaveSettings();
            }
            ImGui.SetNextItemWidth(250);
            if (ImGui.SliderInt("确认后的基础延迟", ref intervalMs, configuration.SubmissionProtection ? 1000 : 100, 10000, "%d 毫秒"))
            {
                intervalMs = Math.Clamp(intervalMs, configuration.SubmissionProtection ? 1000 : 100, 10000);
                coordinator.Reschedule(Environment.TickCount64, intervalMs, configuration.SubmissionProtection);
                saveSettingsAt = Environment.TickCount64 + 500;
            }
            ImGui.TextDisabled("仅在确认上次提交后计时；卡顿不会补发，连续未确认会安全停止。");
            var haltPreCastMs = configuration.HaltPreCastMs;
            ImGui.SetNextItemWidth(250);
            if (ImGui.SliderInt("停手前置阈值", ref haltPreCastMs, 0, 5000, "%d 毫秒"))
            {
                configuration.HaltPreCastMs = haltPreCastMs;
                configuration.Normalize();
                SaveSettings();
            }
            var haltPostCastMs = configuration.HaltPostCastMs;
            ImGui.SetNextItemWidth(250);
            if (ImGui.SliderInt("读条后的停手等待", ref haltPostCastMs, 500, 10000, "%d 毫秒"))
            {
                configuration.HaltPostCastMs = haltPostCastMs;
                configuration.Normalize();
                SaveSettings();
            }
            ImGui.TextDisabled("默认 1800 / 3500 毫秒。修改立即生效，包括当前停手窗口；前置越小越激进，后置过短可能触发 7 秒输入锁定。");

            if (ImGui.Checkbox("防闲置（仅自动模式、游戏前台）", ref antiIdleEnabled))
            {
                if (!antiIdleEnabled) CancelActivity();
                SaveSettings();
            }
            if (antiIdleEnabled)
            {
                if (ImGui.Combo("防闲置方式", ref antiIdleMode, ["错误输入 1 + Enter", "功能键与方向键"], 2))
                {
                    CancelActivity();
                    SaveSettings();
                }
                ImGui.TextDisabled(antiIdleMode == 0 ? antiIdleSubmit.Status : antiIdle.Status);
            }

            ImGui.Separator();
            if (ImGui.Checkbox("自动确认魔光键影“出发”", ref configuration.AutoCommence))
            {
                confirmationFault = false;
                SaveSettings();
            }
            if (ImGui.Checkbox("自动点击结算“退出”", ref configuration.AutoExitResults))
            {
                confirmationFault = false;
                SaveSettings();
            }
            ImGui.TextWrapped(confirmationStatus);

            if (journal != null)
            {
                ImGui.Separator();
                ImGui.TextWrapped("诊断已自动开启；实机异常时提供最新 JSONL 即可。");
                if (ImGui.Button("打开诊断目录")) Process.Start(new ProcessStartInfo("explorer.exe", journal.DirectoryPath) { UseShellExecute = true });
                if (journal.Error != null) ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), journal.Error);
            }

            ImGui.Separator();
            ImGui.TextWrapped(coordinator.LastResult);
            if (ImGui.CollapsingHeader("当前候选目标"))
            {
                if (snapshot != null)
                    foreach (var c in snapshot.Candidates)
                        ImGui.BulletText($"{TargetPolicy.Reason(c)} · {c.Label} → {c.Word} · HP {c.HealthPercent:F0} · 读条 {c.CastProgress:P0}");
            }
        }
        ImGui.End();
        open = visible;
        if (!open) PauseExecution();
    }

    private void SaveSettings()
    {
        configuration.Automatic = automatic;
        configuration.IntervalMs = intervalMs;
        configuration.AntiIdleEnabled = antiIdleEnabled;
        configuration.AntiIdleMode = antiIdleMode;
        configuration.Normalize();
        try
        {
            Pi.SavePluginConfig(configuration);
            saveSettingsAt = 0;
        }
        catch (Exception ex)
        {
            saveSettingsAt = Environment.TickCount64 + 5000;
            Log.Warning(ex, "Could not save KeyboundClick settings; retry scheduled");
        }
    }

    private void PauseExecution()
    {
        gate.Cancel();
        CancelActivity();
        coordinator.Suspend();
        safety.Reset();
        currentSafetyReason = null;
        lastLoggedSafetyReason = null;
    }

    private void CancelActivity()
    {
        antiIdle.Cancel(Environment.TickCount64);
        antiIdleSubmit.Cancel(Environment.TickCount64, Environment.TickCount64);
    }

    public void Dispose()
    {
        disposed = true;
        SaveSettings();
        PauseExecution();
        if (antiIdle.IsBusy || antiIdleSubmit.IsBusy) Log.Error("Unable to release an activity key during unload");
        Framework.Update -= Update;
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenMainUi -= Open;
        Pi.UiBuilder.OpenConfigUi -= Open;
        Commands.RemoveHandler("/ktgclick");
        coordinator.Event -= OnSubmissionEvent;
        journal?.Record("session-end", new { coordinator.Attempts, coordinator.Effects, coordinator.Unmatched, coordinator.Stalls });
        journal?.Dispose();
    }
}
