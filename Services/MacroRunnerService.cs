using AoE4OverlayCS.Models;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Input;

namespace AoE4OverlayCS.Services
{
    /// <summary>宏运行状态通知：消息 + 是否运行中 + 是否为错误。</summary>
    public sealed record MacroRunStatus(string Message, bool IsRunning, bool IsError);

    /// <summary>热键序列中的单步：主键 + 修饰键。</summary>
    public sealed record MacroKeyStep(Key Key, ModifierKeys Modifiers);

    /// <summary>
    /// 按键精灵式热键自动化引擎：
    /// - 通过 SendInput 以扫描码方式后台模拟按键，不激活/切换任何窗口，不抢占游戏焦点；
    /// - 严格按序列顺序与设定间隔执行，间隔/重复次数/序列在运行期间动态生效；
    /// - 循环跑在专用后台线程（同步 Thread.Sleep，不用 Task/计时器/Dispatcher 调度），
    ///   状态经 StatusChanged 事件回报给 UI，绝不因 UI 繁忙而阻塞循环。
    /// </summary>
    public sealed class MacroRunnerService : IDisposable
    {
        private const int HoldMs = 40;   // 按键按下保持时长（毫秒）
        private const int GapMs = 40;    // 相邻按键间隔（毫秒）
        private const int PollMs = 250;  // 倒计时轮询粒度（毫秒）

        public const int MaxKeys = 40;             // 序列最大键数（防止滥用）
        public const int MinIntervalSeconds = 1;   // 最小触发间隔（秒），防止资源占用过高
        public const int SliderMaxIntervalSeconds = 30; // 仅滑条上限；手输可以更大
        public const int MaxRepeatCount = 99;      // 最大重复次数（0 = 无限）
        public const int MaxIdleWaitMs = 5000;     // 操作避让上限（毫秒），0 = 不避让
        public const int GateWindowMs = 500;       // 输入屏蔽窗口时长（毫秒）：从首键起算，注入后补足，期间键鼠输入被吞

        private CancellationTokenSource? _cts;
        private volatile bool _isRunning;
        private int _runGeneration;

        [ThreadStatic]
        private static int _threadRunGeneration;
        private readonly Action<string>? _log;
        private readonly InputGateService? _gate;

        public bool IsRunning => _isRunning;

        /// <summary>状态变化事件（引擎线程触发，订阅方须调度回 UI 线程）。</summary>
        public event Action<MacroRunStatus>? StatusChanged;

        public MacroRunnerService(Action<string>? log = null, InputGateService? gate = null)
        {
            _log = log;
            _gate = gate;
        }

        /// <summary>启动自动化循环。序列为空时报告错误并拒绝启动。</summary>
        public void Start(AppSettings settings)
        {
            if (_isRunning) return;
            if (ParseSequence(settings.MacroSequence).Count == 0)
            {
                Report(L(settings, "热键序列为空，请先配置", "Key sequence is empty, configure it first"), false, true);
                return;
            }

            // 输入闸门钩子仅宏运行期间挂载（Start 由 UI 线程调用），平时对全系统零开销
            _gate?.Start();

            var generation = Interlocked.Increment(ref _runGeneration);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _isRunning = true;
            Report(L(settings, "热键自动化已启动", "Macro automation started"), true, false);
            var thread = new Thread(() => RunLoopSync(settings, ct, generation))
            {
                IsBackground = true,
                Name = "MacroRunner"
            };
            thread.Start();
        }

        /// <summary>停止自动化循环（幂等；未运行时不产生状态回报）。</summary>
        public void Stop(AppSettings settings)
        {
            if (!_isRunning) return;
            _cts?.Cancel();
            _isRunning = false;
            _gate?.Stop();
            Report(L(settings, "已停止", "Stopped"), false, false);
        }

        /// <summary>立即执行一轮序列（测试按钮）。按键发送到当前焦点窗口。</summary>
        public void RunOnce(AppSettings settings)
        {
            if (_isRunning)
            {
                Report(L(settings, "正在执行中，请稍候", "Busy, please wait"), false, true);
                return;
            }
            var steps = ParseSequence(settings.MacroSequence);
            if (steps.Count == 0)
            {
                Report(L(settings, "热键序列为空，请先配置", "Key sequence is empty, configure it first"), false, true);
                return;
            }

            var generation = Interlocked.Increment(ref _runGeneration);
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _isRunning = true;
            _log?.Invoke("test");
            var thread = new Thread(() =>
            {
                _threadRunGeneration = generation;
                try
                {
                    WaitShiftReleased(settings, ct);
                    Report(L(settings, "测试：正在向当前焦点窗口发送按键…", "Test: sending keys to the focused window..."), true, false);
                    ExecuteSequence(steps);
                    Report(L(settings, "测试完成", "Test finished"), false, false);
                }
                catch (OperationCanceledException)
                {
                    if (generation == Volatile.Read(ref _runGeneration))
                        Report(L(settings, "已停止", "Stopped"), false, false);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"test error: {ex}");
                    Report(L(settings, "测试异常，已停止：" + ex.Message, "Test error, stopped: " + ex.Message), false, true);
                }
                finally
                {
                    if (generation == Volatile.Read(ref _runGeneration))
                        _isRunning = false;
                }
            })
            {
                IsBackground = true,
                Name = "MacroRunner-Test"
            };
            thread.Start();
        }

        public void Dispose()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// 解析序列字符串。语法：以 + 分隔；修饰键名（Ctrl/Shift/Alt，含 LeftCtrl 等）
        /// 作用于其后第一个主键，如 "H+Q+Q+Q"、"Ctrl+A+B"。无效记号被忽略；上限 MaxKeys。
        /// </summary>
        public static List<MacroKeyStep> ParseSequence(string? sequence)
        {
            var steps = new List<MacroKeyStep>();
            if (string.IsNullOrWhiteSpace(sequence)) return steps;

            var tokens = sequence.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var pending = ModifierKeys.None;
            foreach (var token in tokens)
            {
                if (steps.Count >= MaxKeys) break;

                if (TryParseModifier(token, out var mod))
                {
                    pending |= mod;
                    continue;
                }

                if (token.Length == 1 && token[0] >= '0' && token[0] <= '9')
                {
                    // 数字键：映射到主键盘数字（常用作游戏控制组编队号，如 4+Q+1）
                    steps.Add(new MacroKeyStep((Key)(Key.D0 + (token[0] - '0')), pending));
                    pending = ModifierKeys.None;
                    continue;
                }

                if (token.Length == 2 && (token[0] == 'R' || token[0] == 'r') && token[1] >= '0' && token[1] <= '9')
                {
                    // R0-R9：右侧小键盘数字键
                    steps.Add(new MacroKeyStep((Key)(Key.NumPad0 + (token[1] - '0')), pending));
                    pending = ModifierKeys.None;
                    continue;
                }

                if (Enum.TryParse(token, true, out Key key) && key != Key.None)
                {
                    if (KeyIsModifier(key, out var keyMod))
                    {
                        pending |= keyMod;
                        continue;
                    }
                    steps.Add(new MacroKeyStep(key, pending));
                    pending = ModifierKeys.None;
                }
                else
                {
                    // 无效记号：丢弃并重置悬挂修饰键，避免 "Ctrl+xyz+H" 意外组合出 Ctrl+H
                    pending = ModifierKeys.None;
                }
            }
            return steps;
        }

        /// <summary>按键人性化显示：主键盘数字显示为 0-9，小键盘数字显示为 R0-R9（右侧键盘），其余用 WPF 枚举名。</summary>
        public static string KeyToDisplay(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9) return ((int)(key - Key.D0)).ToString();
            if (key >= Key.NumPad0 && key <= Key.NumPad9) return "R" + (int)(key - Key.NumPad0);
            return key.ToString();
        }

        /// <summary>把单步格式化为 "Ctrl+Shift+Alt+主键" 展示文本（按键芯片/日志用）。</summary>
        public static string StepToDisplay(MacroKeyStep step)
        {
            var sb = new StringBuilder();
            if ((step.Modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((step.Modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((step.Modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            sb.Append(KeyToDisplay(step.Key));
            return sb.ToString();
        }

        /// <summary>
        /// 主循环（专用后台线程，全同步）。每轮动态读取重复次数与序列；
        /// 轮间倒计时以 250ms 粒度轮询，动态响应间隔修改。
        /// </summary>
        private void RunLoopSync(AppSettings settings, CancellationToken ct, int generation)
        {
            _threadRunGeneration = generation;
            var executed = 0;
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // 每轮动态读取重复次数（0 = 无限）
                    var repeats = Math.Clamp(settings.MacroRepeatCount, 0, MaxRepeatCount);
                    if (repeats > 0 && executed >= repeats) break;

                    // 每轮动态读取序列，修改在下一轮生效
                    var steps = ParseSequence(settings.MacroSequence);
                    if (steps.Count == 0)
                    {
                        Report(L(settings, "热键序列为空，已自动停止", "Key sequence is empty, stopped automatically"), false, true);
                        return;
                    }

                    // 无限循环且开启 Shift 监控：大部分间隔只倒计时，不查 Shift。
                    // 首轮没有间隔，仅当启动时 Shift 正按着才等待；后续轮次的检测在间隔结束前 1 秒才开始。
                    if (repeats == 0 && settings.MacroMonitorShift)
                    {
                        if (executed == 0 || IsShiftDown())
                            WaitShiftReleased(settings, ct);
                    }
                    else
                    {
                        WaitIdle(settings, ct);
                        if (settings.MacroMonitorShift)
                            WaitShiftReleased(settings, ct);
                    }

                    var roundSuffix = repeats > 0 ? $"/{repeats}" : "";
                    Report(L(settings, $"正在发送按键（第 {executed + 1}{roundSuffix} 轮）",
                                   $"Sending keys (round {executed + 1}{roundSuffix})"), true, false);
                    ExecuteSequence(steps);
                    executed++;

                    if (repeats > 0 && executed >= repeats) break;

                    if (repeats == 0 && settings.MacroMonitorShift)
                        WaitIntervalWithLateShiftWatch(settings, executed, ct);
                    else
                        WaitInterval(settings, executed, ct);
                }
                Report(L(settings, "热键序列执行完成", "Macro sequence finished"), false, false);
            }
            catch (OperationCanceledException)
            {
                // 只回应本轮。快按 F10 又启动了新一轮时，旧线程不能把新一轮的状态改成“已停止”。
                if (generation == Volatile.Read(ref _runGeneration))
                    Report(L(settings, "已停止", "Stopped"), false, false);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"run error: {ex}");
                Report(L(settings, "执行异常，已停止：" + ex.Message, "Error, stopped: " + ex.Message), false, true);
            }
            finally
            {
                if (generation == Volatile.Read(ref _runGeneration))
                    _isRunning = false;
            }
        }

        /// <summary>
        /// 无限循环 + Shift 监控的轮间等待。例如间隔 15 秒时，前 14 秒只倒计时；
        /// 最后 1 秒才查询 Shift。这段时间内若 Shift 按住，就等到松开后立刻返回；
        /// 没按住则睡到间隔终点再返回。间隔短于 1 秒时，整段都处于检测窗口。
        /// </summary>
        private void WaitIntervalWithLateShiftWatch(AppSettings settings, int executed, CancellationToken ct)
        {
            var elapsedMs = 0L;
            var lastShown = -1L;
            var shiftReported = false;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var targetMs = IntervalTargetMs(settings);
                var leadMs = Math.Min((long)ShiftWatchLeadMs, targetMs);
                var watchAtMs = targetMs - leadMs;
                var inWatch = settings.MacroMonitorShift && elapsedMs >= watchAtMs;

                if (inWatch && IsShiftDown())
                {
                    if (!shiftReported)
                    {
                        shiftReported = true;
                        Report(L(settings, "检测到 Shift 正在使用，松开后立即发送组合键…",
                                       "Shift is in use, sending combo as soon as it is released..."), true, false);
                    }
                    Thread.Sleep(ShiftWatchPollMs);
                    ct.ThrowIfCancellationRequested();
                    if (!settings.MacroMonitorShift || !IsShiftDown()) return;
                    continue;
                }

                if (elapsedMs >= targetMs) return;

                // 未到检测窗口，或中途关掉监控：只睡到下一个节点，不查询 Shift
                var nextMarkMs = settings.MacroMonitorShift && !inWatch ? watchAtMs : targetMs;
                var sleepMs = (int)Math.Min(inWatch ? ShiftWatchPollMs : (long)PollMs, nextMarkMs - elapsedMs);
                if (sleepMs <= 0) sleepMs = (int)Math.Min((long)PollMs, targetMs - elapsedMs);
                Thread.Sleep(sleepMs);
                ct.ThrowIfCancellationRequested();
                elapsedMs += sleepMs;

                if (shiftReported) continue;
                var remainSec = Math.Max(0L, (targetMs - elapsedMs + 999) / 1000);
                if (remainSec != lastShown)
                {
                    lastShown = remainSec;
                    Report(L(settings, $"第 {executed} 轮完成，{remainSec} 秒后执行下一轮",
                                   $"Round {executed} done, next in {remainSec}s"), true, false);
                }
            }
        }

        /// <summary>轮间倒计时：250ms 粒度轮询，动态响应间隔修改，状态每秒刷新。</summary>
        private void WaitInterval(AppSettings settings, int executed, CancellationToken ct)
        {
            var elapsedMs = 0L;
            var lastShown = -1L;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                // 每次循环重读目标间隔：调小时立即结束等待；最后一段只睡剩余时间，到点就返回
                var targetMs = IntervalTargetMs(settings);
                var remainMs = targetMs - elapsedMs;
                if (remainMs <= 0) return;

                var sleepMs = (int)Math.Min((long)PollMs, remainMs);
                Thread.Sleep(sleepMs);
                ct.ThrowIfCancellationRequested();
                elapsedMs += sleepMs;

                var remainSec = Math.Max(0L, (targetMs - elapsedMs + 999) / 1000);
                if (remainSec != lastShown)
                {
                    lastShown = remainSec;
                    Report(L(settings, $"第 {executed} 轮完成，{remainSec} 秒后执行下一轮",
                                   $"Round {executed} done, next in {remainSec}s"), true, false);
                }
            }
        }

        /// <summary>手输间隔不设上限，只保证至少 1 秒；毫秒用 long，避免大秒数溢出。</summary>
        private static long IntervalTargetMs(AppSettings settings)
        {
            var seconds = Math.Max(settings.MacroIntervalSeconds, MinIntervalSeconds);
            return (long)seconds * 1000L;
        }

        /// <summary>操作避让：系统键鼠空闲不足阈值时推迟注入（每轮动态读取阈值，0 = 不避让）。</summary>
        private void WaitIdle(AppSettings settings, CancellationToken ct)
        {
            var reported = false;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var quietMs = Math.Clamp(settings.MacroIdleWaitMs, 0, MaxIdleWaitMs);
                if (quietMs <= 0) return;
                var idleMs = GetIdleMs();
                if (idleMs >= quietMs) return;
                if (!reported)
                {
                    reported = true;
                    Report(L(settings, "检测到操作，等待空闲后发送…", "Input detected, waiting for idle..."), true, false);
                }
                Thread.Sleep(PollMs);
                ct.ThrowIfCancellationRequested();
            }
        }

        /// <summary>
        /// Shift 监控（勾选时生效，每轮动态读取）。
        /// 没按住就马上返回；仍按住则避让，松开后立刻返回以便马上发送组合键。
        /// 等待中取消勾选也立刻返回。
        /// </summary>
        private void WaitShiftReleased(AppSettings settings, CancellationToken ct)
        {
            var reported = false;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!settings.MacroMonitorShift) return;
                if (!IsShiftDown()) return;

                if (!reported)
                {
                    reported = true;
                    Report(L(settings, "检测到 Shift 正在使用，松开后立即发送组合键…",
                                   "Shift is in use, sending combo as soon as it is released..."), true, false);
                }
                Thread.Sleep(ShiftWatchPollMs);
                ct.ThrowIfCancellationRequested();
            }
        }

        private const int ShiftWatchLeadMs = 1000;
        private const int ShiftWatchPollMs = 100;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;

        /// <summary>物理左 Shift、右 Shift 或通用 Shift 是否仍处于按下。</summary>
        private static bool IsShiftDown() =>
            (GetAsyncKeyState((int)VK_SHIFT) & 0x8000) != 0
            || (GetAsyncKeyState(VK_LSHIFT) & 0x8000) != 0
            || (GetAsyncKeyState(VK_RSHIFT) & 0x8000) != 0;

        /// <summary>同步执行一轮按键序列；屏蔽窗口从首键起算共 GateWindowMs（注入后补足），期间键鼠输入被吞，保证原子完成。</summary>
        private void ExecuteSequence(List<MacroKeyStep> steps)
        {
            if (_gate == null)
            {
                foreach (var step in steps) PressKey(step);
                return;
            }
            _gate.SetSuppress(true);
            try
            {
                var deadline = Environment.TickCount64 + GateWindowMs;
                foreach (var step in steps) PressKey(step);
                // 注入完成后补足屏蔽窗口：序列刚结束的选择状态（已切回部队）不被紧随的玩家输入打断
                var remain = deadline - Environment.TickCount64;
                if (remain > 0) Thread.Sleep((int)remain);
            }
            finally { _gate.SetSuppress(false); }
        }

        /// <summary>修饰键先按下，主键按住 HoldMs 后释放，再逆序释放修饰键。</summary>
        private void PressKey(MacroKeyStep step)
        {
            var vk = (uint)KeyInterop.VirtualKeyFromKey(step.Key);
            if (vk == 0) return;

            var ctrl = (step.Modifiers & ModifierKeys.Control) != 0;
            var shift = (step.Modifiers & ModifierKeys.Shift) != 0;
            var alt = (step.Modifiers & ModifierKeys.Alt) != 0;

            if (ctrl) SendKey(VK_CONTROL, false);
            if (shift) SendKey(VK_SHIFT, false);
            if (alt) SendKey(VK_MENU, false);

            SendKey(vk, false);
            Thread.Sleep(HoldMs);
            SendKey(vk, true);

            if (alt) SendKey(VK_MENU, true);
            if (shift) SendKey(VK_SHIFT, true);
            if (ctrl) SendKey(VK_CONTROL, true);

            Thread.Sleep(GapMs);
        }

        private static bool TryParseModifier(string token, out ModifierKeys modifier)
        {
            modifier = ModifierKeys.None;
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifier = ModifierKeys.Control;
                return true;
            }
            if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifier = ModifierKeys.Shift;
                return true;
            }
            if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifier = ModifierKeys.Alt;
                return true;
            }
            return false;
        }

        private static bool KeyIsModifier(Key key, out ModifierKeys modifier)
        {
            modifier = key switch
            {
                Key.LeftCtrl or Key.RightCtrl => ModifierKeys.Control,
                Key.LeftShift or Key.RightShift => ModifierKeys.Shift,
                Key.LeftAlt or Key.RightAlt => ModifierKeys.Alt,
                _ => ModifierKeys.None
            };
            return modifier != ModifierKeys.None;
        }

        private static bool IsZh(AppSettings s) =>
            string.Equals(s.Language, "zh-CN", StringComparison.OrdinalIgnoreCase);

        private static string L(AppSettings s, string zh, string en) => IsZh(s) ? zh : en;

        private void Report(string message, bool running, bool error)
        {
            // 停止后，仍在睡眠里的旧线程不能再把状态刷回“运行中 / 下一轮倒计时”。
            var threadGeneration = _threadRunGeneration;
            if (threadGeneration != 0 && threadGeneration != Volatile.Read(ref _runGeneration)) return;
            if (running && !_isRunning) return;
            StatusChanged?.Invoke(new MacroRunStatus(message, running, error));
        }

        // ---- SendInput 后台按键模拟（扫描码方式，兼容 DirectX 游戏） ----

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        private const uint VK_SHIFT = 0x10;
        private const uint VK_CONTROL = 0x11;
        private const uint VK_MENU = 0x12; // Alt

        private const uint MAPVK_VK_TO_VSC = 0;

        private static void SendKey(uint vk, bool keyUp)
        {
            uint scan = MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            uint flags = keyUp ? KEYEVENTF_KEYUP : 0;
            if (scan != 0) flags |= KEYEVENTF_SCANCODE;
            if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT { wVk = (ushort)vk, wScan = (ushort)scan, dwFlags = flags, time = 0, dwExtraInfo = IntPtr.Zero }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        // 方向键、Insert/Delete、Win、数字小键盘除号等为扩展键，需要 EXTENDEDKEY 标志
        private static bool IsExtendedKey(uint vk) =>
            (vk >= 0x21 && vk <= 0x28) || // Prior/Next/End/Home/Left/Up/Right/Down
            vk == 0x2D || vk == 0x2E ||   // Insert / Delete
            vk == 0x5B || vk == 0x5C || vk == 0x5D || // LWin / RWin / Apps
            vk == 0x6F || vk == 0x90;     // Numpad Divide / NumLock

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        // ---- 操作避让：读取系统级最后一次键鼠输入时间 ----

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(out LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private static int GetIdleMs()
        {
            if (!GetLastInputInfo(out var info)) return int.MaxValue;
            return unchecked(Environment.TickCount - (int)info.dwTime);
        }
    }
}
