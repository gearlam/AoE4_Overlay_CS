using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AoE4OverlayCS.Services
{
    /// <summary>
    /// 输入闸门：宏序列注入期间屏蔽物理键盘与鼠标输入（按键、点击、滚轮、移动），
    /// 保证组合原子执行；屏蔽窗口补足到设定时长（默认 500ms）后恢复正常。
    /// 通过 WH_KEYBOARD_LL + WH_MOUSE_LL 实现：程序自身注入的事件带 INJECTED 标志直接放行。
    /// 钩子须在 UI 线程安装（回调依赖安装线程的消息循环）。
    /// </summary>
    public sealed class InputGateService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int INJECTED_FLAG = 0x10; // 键盘 LLKHF_INJECTED 与鼠标 MSLLHF_INJECTED 同为 0x10

        private IntPtr _kbHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private LowLevelProc? _kbProc;
        private LowLevelProc? _mouseProc;
        private volatile bool _suppressing;

        public bool Suppressing => _suppressing;

        /// <summary>安装键盘与鼠标钩子（须在 UI 线程调用，幂等）。</summary>
        public void Start()
        {
            Stop();
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var hMod = GetModuleHandle(curModule?.ModuleName);

            _kbProc = KeyboardHookCallback;
            _kbHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _kbProc, hMod, 0);
            _mouseProc = MouseHookCallback;
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        }

        /// <summary>卸载钩子并确保屏蔽窗口关闭（幂等）。</summary>
        public void Stop()
        {
            _suppressing = false;
            if (_kbHookId != IntPtr.Zero) { UnhookWindowsHookEx(_kbHookId); _kbHookId = IntPtr.Zero; }
            if (_mouseHookId != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHookId); _mouseHookId = IntPtr.Zero; }
            _kbProc = null;
            _mouseProc = null;
        }

        /// <summary>开关屏蔽窗口（宏注入线程调用）。</summary>
        public void SetSuppress(bool value) => _suppressing = value;

        public void Dispose() => Stop();

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _suppressing && IsPhysicalKeyboardEvent(lParam))
            {
                return (IntPtr)1; // 屏蔽窗口内吞掉物理按键；程序自身注入的键带 INJECTED 标志照常放行
            }
            return CallNextHookEx(_kbHookId, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _suppressing && IsPhysicalMouseEvent(lParam))
            {
                return (IntPtr)1; // 屏蔽窗口内吞掉物理鼠标事件（含移动/点击/滚轮）
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        private static bool IsPhysicalKeyboardEvent(IntPtr lParam) =>
            (Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam).flags & INJECTED_FLAG) == 0;

        private static bool IsPhysicalMouseEvent(IntPtr lParam) =>
            (Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).flags & INJECTED_FLAG) == 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
