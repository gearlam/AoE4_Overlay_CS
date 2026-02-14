﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using AoE4OverlayCS.ViewModels;
using AoE4OverlayCS.Services;
using System.Windows;
using System;
using System.Diagnostics;
using System.Drawing; // For Icon
using System.Windows.Forms; // For NotifyIcon
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;

namespace AoE4OverlayCS
{
    public partial class MainWindow : Window
    {
        private NotifyIcon? _notifyIcon;
        private bool _isExitRequested;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTrayIcon();
            ApplyVersionToWindowTitle();
        }

        private void ApplyVersionToWindowTitle()
        {
            string? version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(version))
            {
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
            }

            if (string.IsNullOrWhiteSpace(version)) return;

            int plusIndex = version.IndexOf('+', StringComparison.Ordinal);
            if (plusIndex >= 0)
            {
                version = version.Substring(0, plusIndex).Trim();
            }

            if (Title.Contains(version, StringComparison.OrdinalIgnoreCase)) return;

            var title = Title ?? "";
            int close = title.LastIndexOf(')');
            int open = close >= 0 ? title.LastIndexOf('(', close) : -1;
            if (open >= 0 && close > open)
            {
                Title = title.Insert(close, $" {version}");
                return;
            }

            Title = $"{title} ({version})";
        }

        private void InitializeTrayIcon()
        {
            try
            {
                _notifyIcon = new NotifyIcon();
                // Try to load icon from resources or file
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "img", "aoe4_sword_shield.ico");
                if (File.Exists(iconPath))
                {
                    _notifyIcon.Icon = new Icon(iconPath);
                }
                else
                {
                    // Fallback to error icon to make it visible something is wrong, or Application icon
                    _notifyIcon.Icon = SystemIcons.Application;
                }
                
                _notifyIcon.Visible = true;
                _notifyIcon.Text = "AoE4 Overlay";
                _notifyIcon.DoubleClick += (s, args) => 
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                };

                var contextMenu = new ContextMenuStrip();
                contextMenu.Items.Add("Open", null, (s, e) => {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                });
                contextMenu.Items.Add("Exit", null, (s, e) => {
                    if (_notifyIcon != null) _notifyIcon.Visible = false;
                    _isExitRequested = true;
                    if (DataContext is MainViewModel vm) vm.Stop();
                    Close();
                });
                if (_notifyIcon != null) _notifyIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(LogPaths.Get("tray_error.log"), ex.ToString());
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnClosed(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExitRequested)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            _isExitRequested = true;
            if (DataContext is MainViewModel vm) vm.Stop();
            Close();
        }

        private void OpenHtmlFiles_Click(object sender, RoutedEventArgs e)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "html"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "html")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "AoE4_Overlay_CS", "html"))
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path))
                {
                    OpenExplorer(path);
                    return;
                }
            }

            System.Windows.MessageBox.Show("找不到 html 文件夹。");
        }

        private void OpenConfigLogs_Click(object sender, RoutedEventArgs e)
        {
            var logsDir = LogPaths.LogsDirectory;

            if (Directory.Exists(logsDir))
            {
                var logs = Directory.GetFiles(logsDir, "*.log", SearchOption.TopDirectoryOnly);
                if (logs.Length == 0)
                {
                    OpenExplorer(logsDir);
                    return;
                }

                var latest = logs
                    .Select(p => new FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (latest != null)
                {
                    OpenExplorerSelect(latest.FullName);
                }
                else
                {
                    OpenExplorer(logsDir);
                }
            }
        }

        private static void OpenExplorer(string folderPath)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folderPath}\"") { UseShellExecute = true });
            }
            catch { }
        }

        private static void OpenExplorerSelect(string filePath)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
            }
            catch { }
        }

        private void SetLanguageEnglish_Click(object sender, RoutedEventArgs e)
        {
            SetLanguage("en-US");
        }

        private void SetLanguageChinese_Click(object sender, RoutedEventArgs e)
        {
            SetLanguage("zh-CN");
        }

        private static void SetLanguage(string cultureName)
        {
            var culture = new CultureInfo(cultureName);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            var app = System.Windows.Application.Current;
            if (app == null) return;

            System.Collections.ObjectModel.Collection<ResourceDictionary> merged = app.Resources.MergedDictionaries;
            for (int i = merged.Count - 1; i >= 0; i--)
            {
                var src = merged[i].Source?.ToString() ?? "";
                if (src.Contains("Resources/Strings.", StringComparison.OrdinalIgnoreCase))
                {
                    merged.RemoveAt(i);
                }
            }

            var dict = new ResourceDictionary
            {
                Source = new Uri($"Resources/Strings.{cultureName}.xaml", UriKind.Relative)
            };
            merged.Add(dict);
        }
    }
}
