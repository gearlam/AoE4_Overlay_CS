﻿﻿﻿﻿﻿﻿﻿﻿﻿using AoE4OverlayCS.ViewModels;
using System.Windows;

using System.Threading;

namespace AoE4OverlayCS
{
    public partial class App : System.Windows.Application
    {
        private MainViewModel? _viewModel;
        private static Mutex? _mutex;

        public App()
        {
            const string appName = "AoE4OverlayCS_Mutex";
            bool createdNew;
            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                System.Windows.MessageBox.Show("App is already running!");
                Shutdown();
                return;
            }

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
             System.IO.File.WriteAllText("domain_error.log", e.ExceptionObject.ToString());
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
             System.IO.File.WriteAllText("dispatcher_error.log", e.Exception.ToString());
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            _viewModel = new MainViewModel();
            var window = new MainWindow();
            window.DataContext = _viewModel;
            MainWindow = window;
            window.Show();

            _viewModel.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _viewModel?.Stop();
            base.OnExit(e);
        }
    }
}
