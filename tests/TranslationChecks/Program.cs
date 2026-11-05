using AoE4OverlayCS.Models;
using AoE4OverlayCS.Services;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.IO;

if (args.Contains("--diagnose-saved-api"))
{
    var saved = new TranslationStore().LoadSettings();
    if (args.Contains("--system")) saved.ProxyMode = "System";
    if (args.Contains("--direct")) saved.ProxyMode = "Direct";
    using var service = new TranslationService();
    var timer = System.Diagnostics.Stopwatch.StartNew();
    try
    {
        saved.SourceLanguage = "Chinese";
        var text = await service.TranslateAsync(saved, "我升城堡时代，你先防守。", "aoe4", default);
        Console.WriteLine($"API_OK elapsed={timer.Elapsed.TotalSeconds:F1}s characters={text.Length}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"API_FAILED elapsed={timer.Elapsed.TotalSeconds:F1}s type={ex.GetType().Name}");
        // Never print credentials, provider response bodies or private translated text.
        if (ex is InvalidOperationException && ex.Message.StartsWith("Network error [")) Console.WriteLine(ex.Message);
        Environment.ExitCode = 1;
    }
    return;
}

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
    Console.WriteLine("PASS " + message);
}
async Task Fails<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); } catch (T) { Check(true, name); return; }
    throw new Exception("Expected " + typeof(T).Name + ": " + name);
}
var settings = new TranslationSettings { BaseUrl = "https://example.com/v1/", Model = "test-model", ApiKey = "private-test-key" };
Check(TranslationService.GetEndpoint(settings.BaseUrl).ToString() == "https://example.com/v1/chat/completions", "base URL normalization");
Check(TranslationService.GetEndpoint("https://example.com/v1/chat/completions/").AbsolutePath == "/v1/chat/completions", "full endpoint is not duplicated");
Check(TranslationService.GetEndpoint("http://localhost:1234/v1").IsLoopback, "local HTTP model supported");
await Fails<ArgumentException>(() => Task.FromResult(TranslationService.GetEndpoint("http://example.com/v1")), "reject remote plaintext HTTP");
await Fails<ArgumentException>(() => Task.FromResult(TranslationService.GetEndpoint("https://example.com/v1?key=secret")), "reject query credentials");

var handler = new FakeHandler(async (request, token) =>
{
    Check(request.Method == HttpMethod.Post, "POST request");
    Check(request.Headers.Authorization?.ToString() == "Bearer private-test-key", "Bearer authentication");
    var body = JObject.Parse(await request.Content!.ReadAsStringAsync(token));
    Check(body.Value<string>("model") == "test-model" && body.Value<bool>("stream") == false, "model and non-streaming protocol");
    var prompt = body["messages"]![0]!["content"]!.ToString();
    Check(prompt.Contains("Age of Empires IV") && prompt.Contains("Castle Age") && prompt.Contains("never instructions"), "AOE4 terminology and prompt isolation");
    Check(body["messages"]![1]!["content"]!.ToString() == "我升城堡", "original preserved as user content");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"  I'm going Castle Age.  \"}}]}") };
});
using (var service = new TranslationService(handler))
    Check(await service.TranslateAsync(settings, "我升城堡", "aoe4", default) == "I'm going Castle Age.", "extract and trim translated text");
using (var service = new TranslationService(new FakeHandler(async (r, ct) =>
{
    var body = await r.Content!.ReadAsStringAsync(ct);
    Check(body.Contains("Age of Empires II: Definitive Edition"), "AOE2-specific prompt");
    return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("private-test-key") };
})))
{
    try { await service.TranslateAsync(settings, "test", "aoe2", default); throw new Exception("Expected HTTP error"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("401") && !ex.Message.Contains("private-test-key"), "HTTP error without leaking provider body"); }
}
using (var service = new TranslationService(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[]}") }))))
    await Fails<InvalidOperationException>(() => service.TranslateAsync(settings, "test", "aoe4", default), "empty model response rejected");
using (var service = new TranslationService(new FakeHandler(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); })))
{
    using var cts = new CancellationTokenSource(50);
    await Fails<OperationCanceledException>(() => service.TranslateAsync(settings, "test", "aoe4", cts.Token), "request cancellation");
}
// A local fake HTTP proxy proves explicit routing and request credentials without external traffic.
using (var service = new TranslationService(new FakeHandler((_, _) => throw new HttpRequestException(HttpRequestError.SecureConnectionError, "sensitive-provider-detail"))))
{
    try { await service.TranslateAsync(settings, "test", "aoe4", default); throw new Exception("Expected TLS diagnostic"); }
    catch (InvalidOperationException ex)
    {
        Check(ex.Message.Contains("SecureConnectionError") && ex.Message.Contains("Direct") && !ex.Message.Contains("sensitive-provider-detail"), "TLS diagnostics name route/error without private details");
    }
}
using (var service = new TranslationService(new FakeHandler((_, _) => throw new TaskCanceledException())))
    await Fails<TimeoutException>(() => service.TranslateAsync(settings, "test", "aoe4", default), "transport timeout is distinct from user cancellation");
var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
listener.Start();
var proxyPort = ((IPEndPoint)listener.LocalEndpoint).Port;
using (var proxyBudget = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
{
    var proxyTask = Task.Run(async () =>
    {
        using var client = await listener.AcceptTcpClientAsync(proxyBudget.Token);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, false, leaveOpen: true);
        var line = await reader.ReadLineAsync(proxyBudget.Token);
        Check(line != null && line.Contains("http://localhost:1/v1/chat/completions"), "custom HTTP proxy receives absolute target URL");
        string? header;
        var authorization = false;
        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(proxyBudget.Token)))
            if (header == "Authorization: Bearer private-test-key") authorization = true;
        Check(authorization, "custom proxy preserves API authorization");
        var payload = "{\"choices\":[{\"message\":{\"content\":\"Proxy OK\"}}]}";
        var reply = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n{payload}");
        await stream.WriteAsync(reply, proxyBudget.Token);
    });
    using var routedService = new TranslationService();
    var routed = new TranslationSettings { BaseUrl = "http://localhost:1/v1", Model = "fake", ApiKey = "private-test-key",
        ProxyMode = "Custom", ProxyUrl = $"http://127.0.0.1:{proxyPort}" };
    Check(await routedService.TranslateAsync(routed, "test", "aoe4", proxyBudget.Token) == "Proxy OK", "custom proxy round trip");
    await proxyTask;
    routed.ProxyMode = "Direct";
    await Fails<InvalidOperationException>(() => routedService.TranslateAsync(routed, "test", "aoe4", proxyBudget.Token), "switching to direct bypasses configured proxy");
}
listener.Stop();
var directory = Path.Combine(Path.GetTempPath(), "AoETranslationChecks-" + Guid.NewGuid());
try
{
    var store = new TranslationStore(directory);
    Check(store.LoadHistory().Count == 0, "new history empty");
    Check(Directory.Exists(store.HistoryDirectoryPath), "history directory created when history is loaded");
    var legacyRecord = new TranslationRecord { Original = "旧历史", Translated = "Legacy" };
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "history.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new[] { legacyRecord }));
    Check(store.LoadHistory().Single().Id == legacyRecord.Id, "legacy history migrated without losing record IDs");
    Check(File.Exists(Path.Combine(directory, "translation_history", "history.json")) && !File.Exists(Path.Combine(directory, "history.json")), "history moved into dedicated directory; old plaintext removed");
    Check(new TranslationStore().HistoryDirectoryPath == Path.Combine(AppContext.BaseDirectory, "translation_history"), "default history folder is beside executable");
    store.SaveSettings(settings);
    Check(!File.ReadAllText(Path.Combine(directory, "settings.json")).Contains("private-test-key"), "API key absent from plaintext settings");
    Check(new TranslationStore(directory).LoadSettings().ApiKey == settings.ApiKey, "DPAPI round trip across store instances");
    var a = new TranslationRecord { Original = "进攻", Translated = "Attack", Game = "aoe4" };
    var b = new TranslationRecord { Original = "防守", Translated = "Defend", Game = "aoe2" };
    store.SaveHistory(new[] { a, b });
    Check(new TranslationStore(directory).LoadHistory().Count == 2, "history survives reload");
    store.SaveHistory(store.LoadHistory().Where(r => r.Id != a.Id));
    Check(store.LoadHistory().Single().Id == b.Id, "delete single history record");
    store.SaveHistory(Array.Empty<TranslationRecord>());
    Check(store.LoadHistory().Count == 0, "clear history persists");
    File.WriteAllText(Path.Combine(directory, "history.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new[] { a }));
    var blockedPath = Path.Combine(directory, "blocked");
    File.WriteAllText(blockedPath, "not a directory");
    var blockedStore = new TranslationStore(directory, blockedPath);
    try { blockedStore.LoadHistory(); throw new Exception("Expected migration failure"); }
    catch (IOException) { Check(File.Exists(Path.Combine(directory, "history.json")), "failed migration preserves original history"); }
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
Exception? uiError = null;
var uiDirectory = Path.Combine(Path.GetTempPath(), "AoETranslationUI-" + Guid.NewGuid());
var uiThread = new Thread(() =>
{
    try
    {
        var app = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
        foreach (var language in new[] { "en-US", "zh-CN" })
        {
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/AoEOverlay;component/Resources/Strings.{language}.xaml")
            });
            using var model = new AoE4OverlayCS.ViewModels.TranslationViewModel(() => "aoe4", _ => null, new TranslationStore(uiDirectory));
            var settingsWindow = new AoE4OverlayCS.Views.TranslationSettingsWindow(model, () => { });
            settingsWindow.Show();
            Check(settingsWindow.Title != "" && settingsWindow.IsVisible, language + " settings XAML loads");
            settingsWindow.Close();
            var reusable = new TranslationRecord { Original = "防守", Translated = "Defend" };
            model.History.Add(reusable);
            model.CopyHistoryCommand.Execute(reusable);
            Check(System.Windows.Clipboard.GetText() == reusable.Translated, language + " copy history command copies the selected translation");
            var window = new AoE4OverlayCS.Views.TranslationWindow(model, () => { });
            window.Show();
            Check(window.Title != "" && window.IsVisible && window.Topmost, language + " translator XAML loads and is topmost");
            window.UpdateLayout();
            Check(VisualChildren(window).OfType<System.Windows.Controls.Button>().Any(b => b.Command == model.CopyHistoryCommand && b.CommandParameter == reusable), language + " history row exposes a bound copy button");
            window.Close();
        }
        using (var copyModel = new AoE4OverlayCS.ViewModels.TranslationViewModel(() => "aoe4", _ => null,
            new TranslationStore(uiDirectory), new TranslationService(new FakeHandler(async (_, ct) =>
            {
                await Task.Delay(80, ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"  Attack now  \"}}]}") };
            }))))
        {
            copyModel.Settings.BaseUrl = "https://example.com/v1";
            copyModel.Settings.Model = "fake";
            copyModel.Input = "进攻";
            var copyWindow = new AoE4OverlayCS.Views.TranslationWindow(copyModel, () => { });
            copyWindow.Show();
            System.Windows.Clipboard.SetText("unchanged");
            copyModel.TranslateCommand.Execute(null);
            copyWindow.Hide();
            PumpUntil(() => !copyModel.Busy);
            Check(!copyModel.Busy && copyModel.Output == "Attack now", "translation completes while window is hidden");
            Check(System.Windows.Clipboard.GetText() == "Attack now", "successful translation automatically copies trimmed output");
            Check(!copyWindow.IsVisible, "completed translation does not reopen hidden window");
            Check(copyModel.History.First().Translated == "Attack now", "auto-copy retains normal history persistence");
            System.Windows.Clipboard.SetText("keep test clipboard");
            copyModel.TestCommand.Execute(null);
            PumpUntil(() => !copyModel.Busy);
            Check(System.Windows.Clipboard.GetText() == "keep test clipboard", "model connection test does not overwrite clipboard");
            copyModel.TranslateCommand.Execute(null);
            copyModel.Cancel();
            PumpUntil(() => !copyModel.Busy);
            Check(System.Windows.Clipboard.GetText() == "keep test clipboard", "cancelled translation does not overwrite clipboard");
            copyWindow.Close();
        }
        using (var failedModel = new AoE4OverlayCS.ViewModels.TranslationViewModel(() => "aoe4", _ => null,
            new TranslationStore(uiDirectory), new TranslationService(new FakeHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))))))
        {
            failedModel.Settings.BaseUrl = "https://example.com/v1";
            failedModel.Settings.Model = "fake";
            failedModel.Input = "test";
            System.Windows.Clipboard.SetText("keep failed clipboard");
            failedModel.TranslateCommand.Execute(null);
            PumpUntil(() => !failedModel.Busy);
            Check(System.Windows.Clipboard.GetText() == "keep failed clipboard", "failed translation does not overwrite clipboard");
        }
        var hotkeySettings = new AppSettings { OverlayHotkey = "F9" };
        var moduleStore = new TranslationStore(uiDirectory);
        moduleStore.SaveSettings(new TranslationSettings { Enabled = true, Hotkey = "F9" });
        var module = new TranslationModule(hotkeySettings, moduleStore);
        var moduleModel = (AoE4OverlayCS.ViewModels.TranslationViewModel)typeof(TranslationModule)
            .GetField("_model", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(module)!;
        moduleModel.Settings.Enabled = true;
        moduleModel.Settings.Hotkey = "F9";
        module.RefreshHotkey();
        Check(moduleModel.Status.Contains("冲突"), "translation hotkey detects overlay conflict");
        moduleModel.Settings.Hotkey = "A";
        moduleModel.RegisterHotkey();
        Check(moduleModel.Status.Contains("无效"), "plain typing keys cannot be global triggers");
        module.Toggle();
        Check(app.Windows.OfType<AoE4OverlayCS.Views.TranslationWindow>().Any(w => w.IsVisible), "module opens translation window");
        var translator = app.Windows.OfType<AoE4OverlayCS.Views.TranslationWindow>().Single();
        moduleModel.Input = "保留原文";
        moduleModel.Output = "Keep this translation";
        module.Toggle();
        Check(!translator.IsVisible, "module hotkey toggle hides translation window");
        module.Toggle();
        Check(ReferenceEquals(translator, app.Windows.OfType<AoE4OverlayCS.Views.TranslationWindow>().Single()) && translator.IsVisible,
            "hotkey reuses the hidden translation window");
        Check(moduleModel.Input == "保留原文" && moduleModel.Output == "Keep this translation", "hide/show preserves translation content");
        var host = new System.Windows.Window();
        app.MainWindow = host;
        host.Show();
        host.Activate();
        PumpUntil(() => !translator.IsVisible);
        Check(!translator.IsVisible, "clicking away to another window automatically hides translator");
        module.Toggle();
        PumpUntil(() => translator.IsActive);
        Check(translator.IsVisible && translator.IsActive, "hotkey restores and activates translator after focus loss");
        var translatorInput = (System.Windows.Controls.TextBox)translator.FindName("InputBox");
        translatorInput.Focus();
        PumpUntil(() => translatorInput.IsKeyboardFocused);
        Check(translator.IsVisible && translatorInput.IsKeyboardFocused, "focus changes inside translator do not hide it");
        module.OpenSettings();
        var ownedSettings = app.Windows.OfType<AoE4OverlayCS.Views.TranslationSettingsWindow>().Single();
        ownedSettings.Activate();
        PumpUntil(() => ownedSettings.IsActive);
        Check(translator.IsVisible && ownedSettings.IsVisible, "opening owned settings does not hide translator or settings");
        ownedSettings.Close();
        translator.Activate();
        PumpUntil(() => translator.IsActive);
        module.Toggle();
        hotkeySettings.OverlayHotkey = "";
        moduleModel.Settings.Hotkey = "F9";
        module.RefreshHotkey();
        module.OpenSettings();
        var openSettings = app.Windows.OfType<AoE4OverlayCS.Views.TranslationSettingsWindow>().Single();
        openSettings.Activate();
        var focusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var frame = new System.Windows.Threading.DispatcherFrame();
        focusTimer.Tick += (_, _) => { focusTimer.Stop(); frame.Continue = false; };
        focusTimer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        var keyInputs = new[] { new KeyInput { Type = 1, Data = new InputUnion { Key = new KeyboardInput { VirtualKey = 0x78 } } },
            new KeyInput { Type = 1, Data = new InputUnion { Key = new KeyboardInput { VirtualKey = 0x78, Flags = 2 } } } };
        Native.SendInput((uint)keyInputs.Length, keyInputs, System.Runtime.InteropServices.Marshal.SizeOf<KeyInput>());
        focusTimer.Start();
        frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(!translator.IsVisible, "saved F9 is suspended while editing translation settings");
        module.RefreshHotkey();
        Check(!translator.IsVisible, "registration refresh cannot rearm a hotkey while settings are open");
        moduleModel.Settings.Hotkey = "F8"; // Draft only: closing without save must restore F9.
        openSettings.Close();
        Native.SendInput((uint)keyInputs.Length, keyInputs, System.Runtime.InteropServices.Marshal.SizeOf<KeyInput>());
        focusTimer.Start();
        frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(app.Windows.OfType<AoE4OverlayCS.Views.TranslationWindow>().Any(), "closing settings without saving restores the previously applied F9");
        module.Toggle();
        module.OpenSettings();
        var savedSettingsWindow = app.Windows.OfType<AoE4OverlayCS.Views.TranslationSettingsWindow>().Single();
        savedSettingsWindow.Activate();
        ((System.Windows.Controls.TextBox)savedSettingsWindow.FindName("HotkeyBox")).Focus();
        keyInputs[0].Data.Key.VirtualKey = keyInputs[1].Data.Key.VirtualKey = 0x77;
        Native.SendInput((uint)keyInputs.Length, keyInputs, System.Runtime.InteropServices.Marshal.SizeOf<KeyInput>());
        focusTimer.Start();
        frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(moduleModel.Settings.Hotkey == "F8", "recorded F8 updates the bound hotkey draft");
        moduleModel.SaveCommand.Execute(null);
        Check(moduleStore.LoadSettings().Hotkey == "F8", "save persists new hotkey");
        app.Windows.OfType<AoE4OverlayCS.Views.TranslationSettingsWindow>().Single().Close();
        keyInputs[0].Data.Key.VirtualKey = keyInputs[1].Data.Key.VirtualKey = 0x77;
        Native.SendInput((uint)keyInputs.Length, keyInputs, System.Runtime.InteropServices.Marshal.SizeOf<KeyInput>());
        focusTimer.Start();
        frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
        Check(app.Windows.OfType<AoE4OverlayCS.Views.TranslationWindow>().Any(), "saved F8 becomes active after settings close");
        translator.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            System.Windows.PresentationSource.FromVisual(translator), 0, System.Windows.Input.Key.Escape)
            { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
        Check(!translator.IsVisible, "Escape hides translator");
        module.Toggle();
        Check(translator.IsVisible, "hotkey restores translator after Escape");
        module.Stop();
        Check(!app.Windows.OfType<AoE4OverlayCS.Views.TranslationWindow>().Any(), "module stop closes the retained translator");
        app.Shutdown();
    }
    catch (Exception ex) { uiError = ex; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
uiThread.Join();
if (Directory.Exists(uiDirectory)) Directory.Delete(uiDirectory, true);
if (uiError != null) throw new Exception("WPF smoke check failed", uiError);
Console.WriteLine($"All {checks} checks passed.");

static void PumpUntil(Func<bool> done)
{
    var frame = new System.Windows.Threading.DispatcherFrame();
    var budget = System.Diagnostics.Stopwatch.StartNew();
    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
    timer.Tick += (_, _) =>
    {
        if (done() || budget.Elapsed > TimeSpan.FromSeconds(3))
        {
            timer.Stop();
            frame.Continue = false;
        }
    };
    timer.Start();
    System.Windows.Threading.Dispatcher.PushFrame(frame);
}

static IEnumerable<System.Windows.DependencyObject> VisualChildren(System.Windows.DependencyObject parent)
{
    for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
    {
        var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
        yield return child;
        foreach (var descendant in VisualChildren(child)) yield return descendant;
    }
}

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
struct KeyInput { public uint Type; public InputUnion Data; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
struct InputUnion
{
    [System.Runtime.InteropServices.FieldOffset(0)] public KeyboardInput Key;
    [System.Runtime.InteropServices.FieldOffset(0)] public MouseInput Mouse;
}
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr Extra; }
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr Extra; }
static class Native
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint SendInput(uint count, KeyInput[] inputs, int size);
}

sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
}
