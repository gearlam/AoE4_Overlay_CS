using AoE4OverlayCS.Views;
using AoE4OverlayCS.ViewModels;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

internal static class Program
{
    private static int _checks;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAILED: " + name);
        _checks++;
        Console.WriteLine("PASS: " + name);
    }

    [STAThread]
    private static void Main()
    {
        var app = new Application();
        var vm = new MainViewModel(); // Services are not started; no global hooks or network listeners.
        var original = vm.Settings.MacroSequence;
        var view = new SettingsView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 700 };
        void Call(string method, object args) => typeof(SettingsView).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(view, new[] { view, args });
        var text = (TextBox)view.FindName("MacroSequenceTextBox");
        var area = (Border)view.FindName("MacroRecordingArea");
        window.Show();
        var source = PresentationSource.FromVisual(window)!;
        void Press(Key key) => Call("SettingsView_PreviewKeyDown", new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        void Click(MouseButton button) => Call("MacroRecordingArea_PreviewMouseDown", new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button) { RoutedEvent = Mouse.PreviewMouseDownEvent });
        try
        {
            vm.Settings.MacroSequence = "";
            Call("MacroRecordButton_Click", new RoutedEventArgs());
            Check(text.IsReadOnly && area.Visibility == Visibility.Visible && !InputMethod.GetIsInputMethodEnabled(area), "recording uses a read-only sequence and IME-disabled input area");
            Press(Key.H);
            Click(MouseButton.Left);
            Click(MouseButton.Right);
            Click(MouseButton.Middle);
            Check(vm.Settings.MacroSequence == "H+MouseLeft+MouseRight", "recorder appends left/right only and ignores middle click");
            var composition = new TextComposition(InputManager.Current, area, "pinyin中文");
            var input = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            Call("SettingsView_PreviewTextInput", input);
            Check(input.Handled && vm.Settings.MacroSequence == "H+MouseLeft+MouseRight", "IME composition text cannot pollute the recorded sequence");
            Press(Key.ImeProcessed);
            Check(!vm.Settings.MacroSequence.Contains("Processed"), "IME sentinel names are never recorded");
            Press(Key.Back);
            Check(vm.Settings.MacroSequence == "H+MouseLeft", "Backspace removes one complete mouse action");
            vm.Settings.MacroSequence = "Ctrl+H";
            Press(Key.Back);
            Check(vm.Settings.MacroSequence == "", "Backspace removes a full combo without leaving a modifier");
            Press(Key.Escape);
            var stopped = vm.Settings.MacroSequence;
            Click(MouseButton.Left);
            Check(!text.IsReadOnly && area.Visibility == Visibility.Collapsed && vm.Settings.MacroSequence == stopped, "Esc restores editing and stops mouse recording");
            Check(!InputMethod.GetIsInputMethodEnabled(text), "manual sequence input excludes IME composition too");
        }
        finally
        {
            vm.Settings.MacroSequence = original;
            window.Close();
            app.Shutdown();
        }
        Console.WriteLine($"Passed {_checks} recorder checks (no global input injected).");
    }
}
