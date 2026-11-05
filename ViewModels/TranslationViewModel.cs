using AoE4OverlayCS.Models;
using AoE4OverlayCS.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace AoE4OverlayCS.ViewModels
{
    public sealed class TranslationViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly TranslationStore _store;
        private readonly TranslationService _service;
        private readonly Func<string> _game;
        private readonly Func<TranslationSettings, string?> _register;
        private CancellationTokenSource? _request;
        public TranslationSettings Settings { get; private set; } = new();
        public ObservableCollection<TranslationRecord> History { get; } = new();
        public string HistoryDirectoryPath => _store.HistoryDirectoryPath;
        public static string[] ProxyModes { get; } = { "Direct", "System", "Custom" };
        public static string[] Languages { get; } = { "Chinese", "English", "Spanish", "French", "German", "Portuguese", "Russian", "Japanese", "Korean", "Italian", "Turkish", "Arabic", "Hindi", "Vietnamese" };
        private string _input = "", _output = "", _status = "";
        private bool _busy;
        public string Input { get => _input; set { _input = value; Changed(); } }
        public string Output { get => _output; set { _output = value; Changed(); } }
        public string Status { get => _status; set { _status = value; Changed(); } }
        public bool Busy { get => _busy; private set { _busy = value; Changed(); Changed(nameof(CanEdit)); CommandManager.InvalidateRequerySuggested(); } }
        public bool CanEdit => !Busy;
        public ICommand TranslateCommand { get; }
        public ICommand TestCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CopyCommand { get; }
        public ICommand CopyHistoryCommand { get; }
        public ICommand OpenHistoryFolderCommand { get; }
        public event Action? SettingsSaved;
        public ICommand DeleteCommand { get; }
        public ICommand ClearCommand { get; }
        public event PropertyChangedEventHandler? PropertyChanged;
        private string T(string key) => System.Windows.Application.Current.TryFindResource(key)?.ToString() ?? key;

        public TranslationViewModel(Func<string> game, Func<TranslationSettings, string?> register, TranslationStore? store = null, TranslationService? service = null)
        {
            _store = store ?? new TranslationStore();
            _service = service ?? new TranslationService();
            _game = game;
            _register = register;
            try
            {
                Settings = _store.LoadSettings();
                foreach (var record in _store.LoadHistory()) History.Add(record);
            }
            catch (Exception ex) { Status = T("Tr_StorageError") + " " + ex.Message; }
            TranslateCommand = new RelayCommand(async _ => await Run(false), _ => !Busy && !string.IsNullOrWhiteSpace(Input));
            TestCommand = new RelayCommand(async _ => await Run(true), _ => !Busy);
            SaveCommand = new RelayCommand(_ => Save(), _ => !Busy);
            CopyCommand = new RelayCommand(_ => Copy(Output), _ => !string.IsNullOrEmpty(Output));
            CopyHistoryCommand = new RelayCommand(record => Copy(((TranslationRecord)record!).Translated),
                record => record is TranslationRecord r && !string.IsNullOrEmpty(r.Translated));
            OpenHistoryFolderCommand = new RelayCommand(_ => OpenHistoryFolder());
            DeleteCommand = new RelayCommand(record => Delete(record as TranslationRecord), _ => !Busy);
            ClearCommand = new RelayCommand(_ => Clear(), _ => !Busy && History.Count > 0);
        }

        public void RegisterHotkey()
        {
            var error = _register(Settings);
            if (error != null) Status = error;
        }

        private void Save()
        {
            try
            {
                TranslationService.GetEndpoint(Settings.BaseUrl);
                TranslationService.ValidateNetwork(Settings);
                var error = _register(Settings);
                if (error != null) { Status = error; return; }
                _store.SaveSettings(Settings);
                SettingsSaved?.Invoke();
                Status = T("Tr_Saved");
            }
            catch (Exception ex) { Status = T("Tr_Error") + " " + ex.Message; }
        }

        private async Task Run(bool test)
        {
            if (Busy) return;
            Busy = true;
            Status = T("Tr_Working");
            if (!test) Output = "";
            _request = new CancellationTokenSource();
            // Snapshot configuration, input and game before awaiting; settings may change elsewhere.
            var settings = new TranslationSettings { BaseUrl = Settings.BaseUrl, ApiKey = Settings.ApiKey, Model = Settings.Model,
                SourceLanguage = Settings.SourceLanguage, TargetLanguage = Settings.TargetLanguage,
                ProxyMode = Settings.ProxyMode, ProxyUrl = Settings.ProxyUrl, TimeoutSeconds = Settings.TimeoutSeconds };
            if (test) settings.SourceLanguage = "Chinese";
            var original = test ? "我升城堡时代，你先防守。" : Input.Trim();
            var game = _game();
            try
            {
                var translated = await _service.TranslateAsync(settings, original, game, _request.Token);
                _request.Token.ThrowIfCancellationRequested();
                if (test) { Status = T("Tr_TestOk") + " " + translated; return; }
                Output = translated;
                var copied = Copy(translated);
                var record = new TranslationRecord { Original = original, Translated = translated, Game = game,
                    SourceLanguage = settings.SourceLanguage, TargetLanguage = settings.TargetLanguage };
                try
                {
                    _store.SaveHistory(new[] { record }.Concat(History));
                    History.Insert(0, record);
                    if (copied) Status = T("Tr_Copied");
                }
                catch (Exception ex) { Status = T("Tr_StorageError") + " " + ex.Message; }
            }
            catch (TimeoutException ex) { Status = T("Tr_Timeout") + " " + ex.Message; }
            catch (OperationCanceledException) { Status = T("Tr_Cancelled"); }
            catch (Exception ex) { Status = T("Tr_Error") + " " + ex.Message; }
            finally { _request.Dispose(); _request = null; Busy = false; }
        }

        private bool Copy(string text)
        {
            try { System.Windows.Clipboard.SetText(text); Status = T("Tr_Copied"); return true; }
            catch (Exception ex) { Status = T("Tr_Error") + " " + ex.Message; return false; }
        }
        private void OpenHistoryFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(HistoryDirectoryPath);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{HistoryDirectoryPath}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Status = T("Tr_StorageError") + " " + ex.Message; }
        }
        private void Delete(TranslationRecord? record)
        {
            if (record == null) return;
            try { _store.SaveHistory(History.Where(r => r.Id != record.Id)); History.Remove(record); }
            catch (Exception ex) { Status = T("Tr_StorageError") + " " + ex.Message; }
        }
        private void Clear()
        {
            if (System.Windows.MessageBox.Show(T("Tr_ClearConfirm"), T("Tr_Title"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try { _store.SaveHistory(Array.Empty<TranslationRecord>()); History.Clear(); Output = ""; Input = ""; }
            catch (Exception ex) { Status = T("Tr_StorageError") + " " + ex.Message; }
        }
        public void Cancel() => _request?.Cancel();
        public void Dispose() { Cancel(); _service.Dispose(); }
        private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
