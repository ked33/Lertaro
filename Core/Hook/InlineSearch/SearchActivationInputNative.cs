using System.Runtime.InteropServices;

namespace Lertaro.Core.Hook.InlineSearch;

internal static class SearchActivationInputNative
{
    // Both shipped architectures (x64 and arm64) have pointer-sized, 64-bit extra info. The tag
    // distinguishes our replay from third-party injected input and carries the activation generation.
    private const long ReplayTag = 0x4C53000000000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse; // The native union must retain MOUSEINPUT's larger size.
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        internal ushort VirtualKey, ScanCode;
        internal uint Flags, Time;
        internal IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        internal int X, Y;
        internal uint MouseData, Flags, Time;
        internal IntPtr ExtraInfo;
    }

    internal static bool TryGetRequest(IntPtr extraInfo, out int request)
    {
        var value = extraInfo.ToInt64();
        request = (int)(value & int.MaxValue);
        return IntPtr.Size == 8 && (value & unchecked((long)0xFFFFFFFF00000000)) == ReplayTag && request != 0;
    }

    internal static Input CreateInput(int request, SearchActivationInputBuffer.Key key) => new()
    {
        Type = 1, // INPUT_KEYBOARD
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = key.ScanCode == 0 ? (ushort)key.VirtualKey : (ushort)0,
                ScanCode = (ushort)key.ScanCode,
                Flags = (key.Extended ? 1u : 0u) | (key.IsUp ? 2u : 0u) | (key.ScanCode != 0 ? 8u : 0u),
                ExtraInfo = new IntPtr(ReplayTag | (uint)request)
            }
        }
    };

    internal static bool Send(int request, IReadOnlyList<SearchActivationInputBuffer.Key> keys)
    {
        var inputs = new Input[keys.Count + 1];
        for (var i = 0; i < keys.Count; i++) inputs[i] = CreateInput(request, keys[i]);
        inputs[^1] = CreateInput(request, new(SearchActivationInputBuffer.EndOfBatch, 0));
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == inputs.Length) return true;
        Logger.Log($"[SearchActivation] Input replay incomplete: {sent}/{inputs.Length}, error {Marshal.GetLastWin32Error()}.", LogLevel.Warn);
        return false;
    }
}
