using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace KeyboundClick;

internal sealed unsafe class NativeSubmitter : ISubmissionGateway
{
    private const string SubmitSignature = "40 55 56 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 8B 02";
    private static readonly (string Name, string Pattern)[] RequiredSignatures =
    [
        ("词条接口", SubmitSignature),
        ("实体收集", "4C 8B DC 55 53 56 41 54 41 55 49 8D AB ?? ?? ?? ?? 48 81 EC ?? ?? ?? ?? 48 8B 05"),
        ("读条状态", "48 8B C4 53 48 83 EC ?? 48 8B 59"),
        ("分数状态", "40 55 41 56 48 8D 6C 24 ?? 48 81 EC ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 45 ?? 83 B9 ?? ?? ?? ?? ?? 4C 8B F1 0F 84"),
    ];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool SubmitWordDelegate(nint agent, AtkValue* value);

    private readonly SubmitWordDelegate? submit;

    internal NativeSubmitter(ISigScanner scanner, IPluginLog log)
    {
        try
        {
            foreach (var (name, pattern) in RequiredSignatures)
            {
                if (scanner.TryScanText(pattern, out var address))
                {
                    CheckedSignatureCount++;
                    if (pattern == SubmitSignature) submit = Marshal.GetDelegateForFunctionPointer<SubmitWordDelegate>(address);
                }
                else
                {
                    InitializationError = $"当前游戏版本的{name}签名未匹配，提交已禁用";
                    submit = null;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            InitializationError = "词条接口初始化失败";
            submit = null;
            log.Error(ex, InitializationError);
        }
    }

    internal string? InitializationError { get; }
    internal int CheckedSignatureCount { get; }
    internal bool IsAvailable => submit != null && InitializationError == null;

    public bool Submit(string word)
    {
        if (submit == null || !IsAvailable) throw new InvalidOperationException(InitializationError ?? "词条接口不可用");
        var agents = AgentModule.Instance();
        var agent = agents == null ? null : agents->GetAgentByInternalId((AgentId)398);
        if (agent == null) throw new InvalidOperationException("词条输入代理不可用");

        var bytes = Encoding.UTF8.GetBytes(word + '\0');
        fixed (byte* text = bytes)
        {
            var value = new AtkValue { Type = AtkValueType.String };
            value.String.Value = text;
            return submit((nint)agent, &value);
        }
    }
}
