using System.Runtime.InteropServices;

namespace KeyboundClick;

internal sealed class WindowsActivityKeyboard : IActivityKeyboard
{
    private readonly byte[] keyboardState = new byte[256];
    private long foregroundProbeAt;
    private bool foreground;
    private long inputProbeAt;
    private bool userInput;

    public bool IsGameForeground
    {
        get
        {
            var now = Environment.TickCount64;
            if (now < foregroundProbeAt) return foreground;
            foregroundProbeAt = now + 100;
            foreground = IsGameForegroundNow;
            return foreground;
        }
    }

    public bool HasUserInput(ushort ownedKey)
    {
        var now = Environment.TickCount64;
        if (now < inputProbeAt) return userInput;
        inputProbeAt = now + 50;
        if (!GetKeyboardState(keyboardState))
        {
            userInput = true;
            return true;
        }

        userInput = false;
        for (ushort key = 0x08; key < 255; key++)
        {
            if (key == ownedKey || (key == 0x10 && ownedKey is 0xA0 or 0xA1)) continue;
            if ((keyboardState[key] & 0x80) == 0) continue;
            userInput = true;
            break;
        }
        return userInput;
    }

    public bool Press(ushort key) => IsGameForegroundNow && Send(key, false);
    public bool Release(ushort key) => Send(key, true);

    private static bool IsGameForegroundNow
    {
        get
        {
            var window = GetForegroundWindow();
            if (window == 0) return false;
            GetWindowThreadProcessId(window, out var pid);
            return pid == (uint)Environment.ProcessId;
        }
    }

    private static bool Send(ushort key, bool release)
    {
        var inputs = new[] { new Input { Type = 1, Keyboard = new KeyboardInput { Key = key, Flags = release ? 2u : 0u } } };
        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Key;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] state);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
}
