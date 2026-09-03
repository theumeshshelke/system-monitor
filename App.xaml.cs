using System;
using System.Windows;
using System.Windows.Threading;

namespace SystemMonitor
{
    public partial class App : System.Windows.Application
    {
        public App()
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            System.Windows.MessageBox.Show(
                $"System Monitor hit an error and needs to close:\n\n{e.Exception.Message}\n\n{e.Exception}",
                "System Monitor - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
            Shutdown();
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            System.Windows.MessageBox.Show(
                $"System Monitor hit a fatal error:\n\n{ex?.Message}\n\n{ex}",
                "System Monitor - Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
