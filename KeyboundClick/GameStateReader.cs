using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace KeyboundClick;

internal sealed unsafe class GameStateReader(IClientState client, IObjectTable objects, IGameGui gui, IDataManager data)
{
    private static readonly string[] BlockingAddons =
    [
        "SelectYesno", "SelectString", "SelectIconString", "SelectOk", "ContextMenu",
        "ContentsFinderConfirm", "ContentsFinder", "KTGResult", "KTGGameOver", "_KTGGameover",
    ];

    private readonly Dictionary<uint, string> actionNames = [];

    internal bool IsInDuty()
    {
        var main = GameMain.Instance();
        return client.IsLoggedIn && main != null && main->CurrentContentFinderConditionId == 1095;
    }

    internal GameSnapshot? Read(out string status)
    {
        status = string.Empty;
        if (!IsInDuty())
        {
            status = "等待进入魔光键影";
            return null;
        }

        var agents = AgentModule.Instance();
        if (agents == null)
        {
            status = "等待游戏界面";
            return null;
        }

        var hud = (KtgHud*)agents->GetAgentByInternalId((AgentId)397);
        var inputAgent = (KtgInput*)agents->GetAgentByInternalId((AgentId)398);
        if (hud == null || inputAgent == null || inputAgent->Hud != hud || hud->IsShown == 0 || hud->HasDirector == 0)
        {
            status = "等待副本输入界面";
            return null;
        }
        if (hud->Entities.Count is < 0 or > 128)
        {
            status = "词条数据异常，已停止读取";
            return null;
        }

        var addon = gui.GetAddonByName<AtkUnitBase>("KTGInput");
        if (addon == null || !addon->IsVisible || !addon->IsReady)
        {
            status = "等待输入框显示";
            return null;
        }
        var node = addon->GetComponentNodeById(7);
        if (node == null || node->Component == null || node->Component->GetComponentType() != ComponentType.TextInput)
        {
            status = "未找到可用输入框";
            return null;
        }
        var input = (AtkComponentTextInput*)node->Component;
        if (input->OwnerAddon != addon)
        {
            status = "输入框归属异常";
            return null;
        }

        var inputStatus = "输入框已就绪";
        var allowed = true;
        var lockSeconds = 0;
        var values = addon->AtkValues;
        if (values == null || addon->AtkValuesCount < 5 || values[2].Type != AtkValueType.Bool ||
            values[4].Type is not (AtkValueType.Int or AtkValueType.UInt))
        {
            allowed = false;
            inputStatus = "等待游戏输入许可";
        }
        else
        {
            lockSeconds = Math.Max(0, values[4].Int);
            if (!values[2].Bool)
            {
                allowed = false;
                inputStatus = "游戏当前禁止输入";
            }
            if (!input->Enabled)
            {
                allowed = false;
                inputStatus = "游戏已禁用输入框";
            }
        }

        foreach (var name in BlockingAddons)
        {
            var dialog = gui.GetAddonByName<AtkUnitBase>(name);
            if (dialog == null || !dialog->IsVisible) continue;
            allowed = false;
            inputStatus = "确认框或结算界面打开";
            break;
        }

        var player = objects.LocalPlayer;
        if (player == null || player.IsDead)
        {
            allowed = false;
            inputStatus = "当前无法输入";
        }
        else if (player.StatusList.Any(effect => effect.StatusId == 5347))
        {
            allowed = false;
            inputStatus = "当前受到禁止输入状态影响";
        }

        var ui = RaptureAtkModule.Instance();
        if (ui == null)
        {
            allowed = false;
            inputStatus = "等待输入焦点";
        }
        else
        {
            var target = ui->AtkModule.TextInput.TargetTextInputEventInterface;
            if (target != null && target != &input->AtkTextInputEventInterface)
            {
                allowed = false;
                inputStatus = "其他输入框正在使用";
            }
        }

        var raw = VisibleText(input->RawString);
        var evaluated = VisibleText(input->EvaluatedString);
        var inputText = string.Empty;
        if (raw == null || evaluated == null)
        {
            allowed = false;
            inputStatus = "输入内容读取异常";
        }
        else
        {
            inputText = raw.Length != 0 ? raw : evaluated;
            if (raw.Length != 0 && evaluated.Length != 0 && raw != evaluated)
            {
                allowed = false;
                inputStatus = "输入内容正在变化";
            }
        }

        var candidates = new List<Candidate>();
        foreach (var pair in hud->Entities)
        {
            var entity = pair.Item2;
            if (entity.Disposed != 0 || entity.IsVisible == 0) continue;
            var obj = objects.SearchByEntityId(entity.EntityId);
            if (obj == null) continue;
            var role = EnemyKinds.GetRole(obj.BaseId, entity.Kind);
            if (role == CandidateRole.Unknown) continue;
            if (!obj.IsTargetable && !obj.IsDead && role != CandidateRole.Barrier) continue;

            var name = ReadText(entity.Name);
            var title = ReadText(entity.Title);
            var word = !string.IsNullOrWhiteSpace(name) ? name : title;
            if (string.IsNullOrWhiteSpace(word)) continue;

            var battle = obj as IBattleChara;
            var casting = battle?.IsCasting == true;
            var castActionId = casting ? battle!.CastActionId : 0;
            var remaining = casting ? Math.Max(0, battle!.TotalCastTime - battle.CurrentCastTime) : 0;
            candidates.Add(new Candidate(
                entity.EntityId,
                obj.BaseId,
                obj.Name.TextValue,
                word,
                entity.Kind,
                role == CandidateRole.Boss,
                role,
                float.IsFinite(entity.HealthPercent) ? entity.HealthPercent : 0,
                float.IsFinite(entity.Y) ? entity.Y : 0,
                float.IsFinite(entity.CastProgress) ? entity.CastProgress : 0,
                castActionId,
                remaining,
                ReadText(entity.CastActionName),
                obj.IsDead));
        }

        var haltCasts = new List<CastSnapshot>();
        foreach (var battle in objects.OfType<IBattleChara>())
        {
            if (!battle.IsCasting || !IsHaltAction(battle.CastActionId)) continue;
            haltCasts.Add(new CastSnapshot(
                battle.EntityId,
                battle.CastActionId,
                ActionName(battle.CastActionId),
                battle.CurrentCastTime,
                battle.TotalCastTime));
        }

        status = inputStatus;
        return new GameSnapshot(
            hud->Stage,
            hud->Score,
            hud->ScoreBonusTime,
            hud->IsFever != 0,
            allowed,
            lockSeconds,
            inputStatus,
            inputText,
            (long)input,
            float.IsFinite(inputAgent->IdleSeconds) ? inputAgent->IdleSeconds : 0,
            candidates.ToArray(),
            haltCasts.ToArray());
    }

    private string ActionName(uint actionId)
    {
        if (actionNames.TryGetValue(actionId, out var cached)) return cached;
        var name = data.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(actionId)?.Name.ToString();
        name = string.IsNullOrWhiteSpace(name) ? $"动作 {actionId}" : name;
        actionNames[actionId] = name;
        return name;
    }

    private static bool IsHaltAction(uint actionId) => actionId is 49529 or 49532 or 49702;

    private static string ReadText(Utf8String text)
    {
        if (text.StringPtr.Value == null || text.BufUsed <= 1 || text.BufUsed > 4096) return string.Empty;
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(text.StringPtr, (int)text.BufUsed - 1));
    }

    internal static string? VisibleText(Utf8String text)
    {
        if (text.BufUsed <= 1) return string.Empty;
        if (text.StringPtr.Value == null || text.BufUsed > 4096) return null;
        return SeString.Parse(new ReadOnlySpan<byte>(text.StringPtr, (int)text.BufUsed - 1)).TextValue;
    }
}
