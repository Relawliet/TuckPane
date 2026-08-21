using Microsoft.UI.Dispatching;

namespace TuckPane.Services;

public sealed class OutsideClickHook : IDisposable
{
    private readonly IntPtr _window;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _outsideClick;
    private readonly NativeMethods.HookProc _callback;
    private readonly NativeMethods.HookProc _keyboardCallback;
    private IntPtr _hook;
    private IntPtr _keyboardHook;
    private bool _suppressUntilButtonUp;
    private bool _stopPending;

    public OutsideClickHook(IntPtr window, DispatcherQueue dispatcher, Action outsideClick)
    {
        _window = window;
        _dispatcher = dispatcher;
        _outsideClick = outsideClick;
        _callback = OnMouse;
        _keyboardCallback = OnKeyboard;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _callback, NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            AppLogger.Error("无法安装外部点击捕获钩子。");
        }
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardCallback, NativeMethods.GetModuleHandle(null), 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            AppLogger.Error("无法安装 Esc 捕获钩子。");
        }
    }

    public void Stop()
    {
        if (_suppressUntilButtonUp)
        {
            _stopPending = true;
            return;
        }
        StopNow();
    }

    public void Dispose() => StopNow();

    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0 || _hook == IntPtr.Zero)
        {
            return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
        }

        int message = wParam.ToInt32();
        bool isDown = message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN;
        bool isUp = message is NativeMethods.WM_LBUTTONUP or NativeMethods.WM_RBUTTONUP or NativeMethods.WM_MBUTTONUP;

        if (_suppressUntilButtonUp)
        {
            if (isUp)
            {
                _suppressUntilButtonUp = false;
                if (_stopPending)
                {
                    StopNow();
                }
            }
            return new IntPtr(1);
        }

        if (isDown && NativeMethods.GetWindowRect(_window, out NativeMethods.RECT windowRect))
        {
            NativeMethods.MSLLHOOKSTRUCT data = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            bool outside = data.Point.X < windowRect.Left || data.Point.X >= windowRect.Right || data.Point.Y < windowRect.Top || data.Point.Y >= windowRect.Bottom;
            if (outside)
            {
                _suppressUntilButtonUp = true;
                _ = _dispatcher.TryEnqueue(() => _outsideClick());
                return new IntPtr(1);
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _keyboardHook != IntPtr.Zero &&
            wParam.ToInt32() is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN &&
            System.Runtime.InteropServices.Marshal.ReadInt32(lParam) == NativeMethods.VK_ESCAPE)
        {
            _ = _dispatcher.TryEnqueue(() => _outsideClick());
            return new IntPtr(1);
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void StopNow()
    {
        _stopPending = false;
        if (_hook == IntPtr.Zero && _keyboardHook == IntPtr.Zero)
        {
            return;
        }
        if (_hook != IntPtr.Zero)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        if (_keyboardHook != IntPtr.Zero)
        {
            _ = NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }
}
