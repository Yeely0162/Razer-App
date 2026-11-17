using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace RazerChromaWLEDConnect
{
    /// <summary>
    /// Single instance guard plus a last resort crash log. Without the mutex a second copy
    /// would open a second Chroma connection and fight the first one for the same App ID.
    /// </summary>
    public partial class App : Application
    {
        private const string MutexName = "RazerChromaWLEDStudio.SingleInstance";
        private const string ActivateEventName = "RazerChromaWLEDStudio.Activate";

        private Mutex _mutex;
        private EventWaitHandle _activateSignal;
        private Thread _activateListener;
        private volatile bool _listening;

        protected override void OnStartup(StartupEventArgs e)
        {
            bool createdNew;

            try
            {
                this._mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch
            {
                createdNew = true;
            }

            if (!createdNew)
            {
                // Another copy owns the mutex: wake it up and leave without showing anything.
                try
                {
                    EventWaitHandle existing = EventWaitHandle.OpenExisting(ActivateEventName);
                    existing.Set();
                    existing.Dispose();
                }
                catch
                {
                }

                Shutdown();
                return;
            }

            this.DispatcherUnhandledException += this.OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += this.OnDomainException;

            this.StartActivateListener();
            base.OnStartup(e);
        }

        private void StartActivateListener()
        {
            try
            {
                this._activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            }
            catch
            {
                return;
            }

            this._listening = true;
            this._activateListener = new Thread(this.ListenForActivation);
            this._activateListener.IsBackground = true;
            this._activateListener.Name = "Single instance listener";
            this._activateListener.Start();
        }

        private void ListenForActivation()
        {
            while (this._listening)
            {
                bool signalled;

                try
                {
                    signalled = this._activateSignal.WaitOne(500);
                }
                catch
                {
                    return;
                }

                if (!signalled) continue;

                this.Dispatcher.BeginInvoke(new Action(delegate
                {
                    var window = this.MainWindow as MainWindow;
                    if (window != null) window.ActivateFromTray();
                }));
            }
        }

        private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            this.LogCrash("Dispatcher", e.Exception);

            // The render pipeline, the Chroma callback and device refreshes all guard
            // themselves. Anything that still lands here came from a UI gesture, so keep the
            // app alive instead of dropping the user's session.
            e.Handled = true;
        }

        private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            this.LogCrash("AppDomain", e.ExceptionObject as Exception);
        }

        private void LogCrash(string source, Exception exception)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
                string text = string.Concat(
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    "  [", source, "]",
                    Environment.NewLine,
                    exception == null ? "(no exception object)" : exception.ToString(),
                    Environment.NewLine,
                    Environment.NewLine);

                File.AppendAllText(path, text);
            }
            catch
            {
            }
        }

        private void AppExit(object sender, ExitEventArgs e)
        {
            this._listening = false;

            try
            {
                if (this._activateSignal != null) this._activateSignal.Dispose();
            }
            catch
            {
            }

            try
            {
                if (this._mutex != null)
                {
                    this._mutex.ReleaseMutex();
                    this._mutex.Dispose();
                }
            }
            catch
            {
            }
        }
    }
}
