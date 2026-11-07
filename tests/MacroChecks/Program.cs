using AoE4OverlayCS.Models;
using AoE4OverlayCS.Services;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static void Main()
    {
        SequenceValidation();
        HookFailures();
        HookFiltering();
        ProtectedRun(false);
        ProtectedRun(true);
        StopDuringRound(false);
        StopDuringRound(true);
        StopBeforeRound();
        StopDuringPadding();
        Shutdown();
        RoundInterval();
        InjectionFailure();
        Console.WriteLine($"Passed {_checks} macro checks (no real hooks installed or keys sent).");
    }

    private static AppSettings Settings() => new()
    {
        MacroSequence = "Ctrl+H+Q+Q", MacroIdleWaitMs = 0,
        MacroRepeatCount = 1, MacroIntervalSeconds = 1, Language = "en-US"
    };

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }

    private static void Wait(Func<bool> condition)
    {
        if (!SpinWait.SpinUntil(condition, 5000)) throw new TimeoutException("Macro test timed out");
    }

    private sealed class Hooks
    {
        public int FailAt;
        public int Installs;
        public readonly ConcurrentDictionary<int, InputGateService.LowLevelProc> Callbacks = new();
        public readonly ConcurrentDictionary<IntPtr, bool> Active = new();
        public int Unhooks;
        public InputGateService Create() => new((kind, callback, _, _) =>
        {
            Interlocked.Increment(ref Installs);
            if (kind == FailAt)
            {
                Marshal.SetLastPInvokeError(5);
                return IntPtr.Zero;
            }
            var handle = new IntPtr(Installs);
            Callbacks[kind] = callback;
            Active[handle] = true;
            return handle;
        }, handle =>
        {
            Active.TryRemove(handle, out _);
            Interlocked.Increment(ref Unhooks);
            return true;
        });
    }

    private static void SequenceValidation()
    {
        var steps = MacroRunnerService.ParseSequence("Ctrl+H+MouseLeft+Q+MouseWheelUp+1+R1");
        Check(steps.SequenceEqual(new[]
        {
            new MacroKeyStep(Key.H, ModifierKeys.Control), new MacroKeyStep(Key.Q, ModifierKeys.None),
            new MacroKeyStep(Key.D1, ModifierKeys.None), new MacroKeyStep(Key.NumPad1, ModifierKeys.None)
        }), "sequence stays keyboard-only with main/numpad and modifier mapping intact");
        foreach (var once in new[] { false, true })
        {
            var hooks = new Hooks();
            using var gate = hooks.Create();
            var sent = 0;
            using var runner = new MacroRunnerService(null, gate, (_, _) => sent++);
            var settings = Settings();
            settings.MacroSequence = "";
            if (once) runner.RunOnce(settings); else runner.Start(settings);
            Check(!runner.IsRunning && hooks.Installs == 0 && sent == 0, "empty sequence installs no hooks and sends nothing");
        }
    }

    private static void HookFailures()
    {
        foreach (var failure in new[] { 13, 14 })
        foreach (var once in new[] { false, true })
        {
            var hooks = new Hooks { FailAt = failure };
            using var gate = hooks.Create();
            var sent = 0;
            using var runner = new MacroRunnerService(null, gate, (_, _) => Interlocked.Increment(ref sent));
            MacroRunStatus? status = null;
            runner.StatusChanged += s => status = s;
            if (once) runner.RunOnce(Settings()); else runner.Start(Settings());
            Check(!runner.IsRunning && sent == 0 && status is { IsError: true, IsRunning: false }, $"hook {failure} failure refuses {(once ? "test" : "loop")}");
            Check(status!.Message.Contains("(5:") && status.Message.Contains(failure == 13 ? "Keyboard" : "Mouse"), "installation error identifies device and native reason");
            Check(!gate.Suppressing && hooks.Active.IsEmpty && hooks.Unhooks == (failure == 14 ? 1 : 0), "partial installation rolled back");
            var threw = false;
            try { gate.SetSuppress(true); } catch (InvalidOperationException) { threw = true; }
            Check(threw, "cannot suppress without both hooks");
        }
    }

    private static void HookFiltering()
    {
        var hooks = new Hooks();
        using var gate = hooks.Create();
        gate.Start();
        gate.SetSuppress(true);
        // Raw native layouts on x64: KBDLLHOOKSTRUCT.flags at 8; MSLLHOOKSTRUCT.flags at 12.
        var ptr = Marshal.AllocHGlobal(32);
        try
        {
            foreach (var kind in new[] { 13, 14 })
            {
                for (var i = 0; i < 32; i++) Marshal.WriteByte(ptr, i, 0);
                var callback = hooks.Callbacks[kind];
                var flagOffset = kind == 13 ? 8 : 12;
                Check(callback(0, IntPtr.Zero, ptr) == (IntPtr)1, $"physical {(kind == 13 ? "keyboard" : "mouse")} blocked");
                Marshal.WriteInt32(ptr, flagOffset, kind == 13 ? 0x10 : 0x01);
                Check(callback(0, IntPtr.Zero, ptr) != (IntPtr)1, "injected event passes with correct native flag");
                Marshal.WriteInt32(ptr, flagOffset, kind == 13 ? 0x01 : 0x10);
                Check(callback(0, IntPtr.Zero, ptr) == (IntPtr)1, "unrelated flag does not bypass input protection");
                Check(callback(-1, IntPtr.Zero, ptr) != (IntPtr)1, "negative hook codes pass through");
                gate.SetSuppress(false);
                Check(callback(0, IntPtr.Zero, ptr) != (IntPtr)1, "outside a round physical input passes");
                gate.SetSuppress(true);
            }
        }
        finally { Marshal.FreeHGlobal(ptr); }
        gate.Stop();
        gate.Stop();
        Check(!gate.Suppressing && hooks.Active.IsEmpty && hooks.Unhooks == 2, "cleanup is idempotent");
    }

    private static void ProtectedRun(bool once)
    {
        var hooks = new Hooks();
        using var gate = hooks.Create();
        var events = new ConcurrentQueue<(uint Key, bool Up)>();
        var unprotected = false;
        using var runner = new MacroRunnerService(null, gate, (key, up) =>
        {
            if (!gate.Suppressing || hooks.Active.Count != 2) unprotected = true;
            events.Enqueue((key, up));
        });
        var final = new ManualResetEventSlim();
        var finalClean = false;
        runner.StatusChanged += status =>
        {
            if (status.IsRunning) return;
            finalClean = !gate.Suppressing && hooks.Active.IsEmpty && !runner.IsRunning;
            final.Set();
        };
        var time = Stopwatch.StartNew();
        if (once) runner.RunOnce(Settings()); else runner.Start(Settings());
        Check(final.Wait(5000), "run finishes");
        Check(!unprotected && hooks.Installs == 2, $"{(once ? "test" : "loop")} installs both hooks before sending");
        Check(time.ElapsedMilliseconds >= MacroRunnerService.GateWindowMs - 5, "short round pads protection to 500ms");
        Check(events.ToArray().SequenceEqual(new (uint, bool)[] { (0x11, false), (0x48, false), (0x48, true), (0x11, true), (0x51, false), (0x51, true), (0x51, false), (0x51, true) }), "keyboard order and modifier release preserved");
        Check(finalClean, "finished status is emitted only after cleanup");
    }

    private static void StopDuringRound(bool once)
    {
        var settings = Settings();
        settings.MacroSequence = string.Join('+', Enumerable.Repeat("Q", 10));
        settings.MacroRepeatCount = 0;
        var hooks = new Hooks();
        using var gate = hooks.Create();
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var downs = 0;
        var ups = 0;
        var unprotected = false;
        using var runner = new MacroRunnerService(null, gate, (_, up) =>
        {
            if (!gate.Suppressing) unprotected = true;
            if (up) Interlocked.Increment(ref ups);
            else if (Interlocked.Increment(ref downs) == 1)
            {
                entered.Set();
                if (!resume.Wait(5000)) throw new TimeoutException();
            }
        });
        if (once) runner.RunOnce(settings); else runner.Start(settings);
        try
        {
            Check(entered.Wait(5000), "round starts");
            var completion = runner.StopAsync(settings);
            runner.Stop(settings);
            runner.Start(settings);
            runner.RunOnce(settings);
            Check(runner.IsRunning && gate.Suppressing && hooks.Active.Count == 2 && hooks.Installs == 2 && !completion.IsCompleted, "stop retains protection and rejects overlapping restart/test");
            resume.Set();
            Check(completion.Wait(5000), "stop eventually completes");
            Check(downs == 10 && ups == 10 && !unprotected, "stop finishes long round and releases every key under protection");
            Check(!runner.IsRunning && !gate.Suppressing && hooks.Active.IsEmpty, "stopped round cleans up before reopening startup");
            settings.MacroRepeatCount = 1;
            runner.RunOnce(settings);
            Wait(() => !runner.IsRunning);
            Check(downs == 20 && ups == 20 && hooks.Installs == 4 && !unprotected, "restart after cleanup has independent protection");
        }
        finally { resume.Set(); runner.StopAsync(settings).Wait(5000); }
    }

    private static void StopBeforeRound()
    {
        var settings = Settings();
        var hooks = new Hooks();
        using var gate = hooks.Create();
        var sent = 0;
        using var runner = new MacroRunnerService(null, gate, (_, _) => sent++);
        runner.StatusChanged += status =>
        {
            if (status.Message.StartsWith("Sending keys")) runner.Stop(settings);
        };
        runner.Start(settings);
        Wait(() => !runner.IsRunning);
        Check(sent == 0 && !gate.Suppressing && hooks.Active.IsEmpty, "stop just before the round prevents any injection");
    }

    private static void StopDuringPadding()
    {
        var settings = Settings();
        settings.MacroSequence = "Q";
        var hooks = new Hooks();
        using var gate = hooks.Create();
        using var released = new ManualResetEventSlim();
        using var runner = new MacroRunnerService(null, gate, (_, up) => { if (up) released.Set(); });
        var time = Stopwatch.StartNew();
        runner.RunOnce(settings);
        Check(released.Wait(5000), "short sequence releases its key");
        var stopped = runner.StopAsync(settings);
        Check(gate.Suppressing && runner.IsRunning && !stopped.IsCompleted, "stop does not interrupt protection padding");
        Check(stopped.Wait(5000) && time.ElapsedMilliseconds >= MacroRunnerService.GateWindowMs - 5, "stop honors full 500ms minimum");
    }

    private static void Shutdown()
    {
        var settings = Settings();
        var hooks = new Hooks();
        using var gate = hooks.Create();
        using var entered = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        using var runner = new MacroRunnerService(null, gate, (_, up) =>
        {
            if (!up && !entered.IsSet)
            {
                entered.Set();
                if (!resume.Wait(5000)) throw new TimeoutException();
            }
        });
        runner.Start(settings);
        try
        {
            Check(entered.Wait(5000), "shutdown test enters round");
            var stopped = runner.StopAsync(settings);
            runner.Dispose();
            Check(!stopped.IsCompleted && gate.Suppressing, "shutdown waits asynchronously while input remains protected");
            resume.Set();
            Check(stopped.Wait(5000) && hooks.Active.IsEmpty && !gate.Suppressing, "shutdown completion implies fully restored input");
            runner.Start(settings);
            runner.RunOnce(settings);
            Check(!runner.IsRunning && hooks.Installs == 2, "shutdown permanently rejects new loop or test");
        }
        finally { resume.Set(); runner.StopAsync(settings).Wait(5000); }
    }

    private static void RoundInterval()
    {
        var settings = Settings();
        settings.MacroRepeatCount = 0;
        var hooks = new Hooks();
        using var gate = hooks.Create();
        using var interval = new ManualResetEventSlim();
        var downs = 0;
        using var runner = new MacroRunnerService(null, gate, (_, up) => { if (!up) Interlocked.Increment(ref downs); });
        runner.StatusChanged += status => { if (status.Message.StartsWith("Round 1 done")) interval.Set(); };
        runner.Start(settings);
        Check(interval.Wait(5000), "loop reaches interval");
        Check(!gate.Suppressing && hooks.Active.Count == 2, "interval allows physical keyboard and mouse");
        Check(runner.StopAsync(settings).Wait(5000) && downs == 4 && hooks.Active.IsEmpty, "stop during interval never starts another round");
    }

    private static void InjectionFailure()
    {
        foreach (var failOnRelease in new[] { false, true })
        {
            var hooks = new Hooks();
            using var gate = hooks.Create();
            var events = new ConcurrentQueue<(uint Key, bool Up)>();
            var cleanRelease = true;
            using var runner = new MacroRunnerService(null, gate, (key, up) =>
            {
                if (!gate.Suppressing) cleanRelease = false;
                events.Enqueue((key, up));
                if (key == 0x48 && up == failOnRelease) throw new InvalidOperationException("simulated SendInput failure");
            });
            var done = new ManualResetEventSlim();
            MacroRunStatus? final = null;
            runner.StatusChanged += s => { if (!s.IsRunning) { final = s; done.Set(); } };
            runner.RunOnce(Settings());
            Check(done.Wait(5000) && final is { IsError: true }, "injection failure reports error");
            Check(events.Any(e => e == (0x48u, true)) && events.Any(e => e == (0x11u, true)) && cleanRelease, "failure still attempts main key and modifier releases under protection");
            Check(!runner.IsRunning && !gate.Suppressing && hooks.Active.IsEmpty, "error clears suppression and both hooks");
        }
    }
}
