using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using RazerChromaWLEDConnect.Base;
using RazerChromaWLEDConnect.Core;
using RazerChromaWLEDConnect.Services;
using RazerChromaWLEDConnect.ViewModels;

namespace RazerChromaWLEDConnect
{
    /// <summary>
    /// Shell window. Only UI plumbing lives here: the model owns the devices, the render
    /// pipeline, the settings and the Chroma link.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly HardwareServices _hardware;
        private readonly ChromaBroadcastLink _link;
        private readonly MdnsDiscoveryService _discovery;
        private readonly MainViewModel _model;
        private readonly PowerEventWatcher _power;
        private readonly DispatcherTimer _uiTimer;

        private bool _quitRequested;
        private bool _trayHintShown;

        public MainWindow()
        {
            this.InitializeComponent();

            this._hardware = new HardwareServices();
            this._settings = AppSettings.Load(null, new[] { typeof(LenovoKeyboard) });
            this._settings.RunAtBoot = this._hardware.IsStartupRegistered;

            var slot = new FrameSlot();
            this._link = new ChromaBroadcastLink(slot);
            this._discovery = new MdnsDiscoveryService();

            this._model = new MainViewModel(
                this._settings,
                this._hardware,
                this._link,
                this._discovery,
                slot,
                new WpfUiDispatcher(this.Dispatcher));
            this.DataContext = this._model;

            this._power = new PowerEventWatcher();
            this._power.Suspending += this.PowerSuspending;
            this._power.Resumed += this.PowerResumed;
            this._power.Start();

            this._uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            this._uiTimer.Tick += this.UiTimerTick;
            this._uiTimer.Start();

            this._model.Initialize();

            if (this._settings.RunAtBoot)
            {
                this.WindowState = WindowState.Minimized;
            }
        }

        private void UiTimerTick(object sender, EventArgs e)
        {
            this._model.Refresh();
            this._model.FlushSettings();
        }

        private void PowerSuspending(object sender, EventArgs e)
        {
            this.Dispatcher.BeginInvoke(new Action(delegate
            {
                this._model.Pipeline.Stop();
            }));
        }

        private void PowerResumed(object sender, EventArgs e)
        {
            this.Dispatcher.BeginInvoke(new Action(delegate
            {
                if (this._model.SyncEnabled) this._model.Pipeline.Start();
            }));
        }

        private void WindowClosing(object sender, CancelEventArgs e)
        {
            if (!this._quitRequested)
            {
                // Closing minimises to the tray; quitting lives in the tray menu and asks first.
                e.Cancel = true;
                this.Hide();

                if (!this._trayHintShown)
                {
                    this._trayHintShown = true;
                    this.TrayIcon.ShowBalloonTip(
                        "Still running",
                        "Razer Chroma WLED Studio keeps syncing in the tray. Right click the icon to quit.",
                        Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
                }

                return;
            }

            this.Shutdown();
        }

        private void WindowStateChanged(object sender, EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized) this.Hide();
        }

        private void Shutdown()
        {
            try
            {
                this._uiTimer.Stop();
                this._power.Dispose();
                this._model.Dispose();
            }
            catch
            {
            }

            Application.Current.Shutdown();
        }

        /// <summary>Brings the window back from the tray. Called by the tray icon and by a second launch.</summary>
        public void ActivateFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            this.Topmost = true;
            this.Topmost = false;
            this.Focus();
        }

        private void TrayShow(object sender, RoutedEventArgs e)
        {
            this.ActivateFromTray();
        }

        private void TrayToggleSync(object sender, RoutedEventArgs e)
        {
            this._model.SyncEnabled = !this._model.SyncEnabled;
            this.UpdateTrayText();
        }

        private void TrayMenuOpened(object sender, RoutedEventArgs e)
        {
            this.UpdateTrayText();
        }

        private void UpdateTrayText()
        {
            this.TraySyncItem.Header = this._model.SyncEnabled ? "Pause sync" : "Resume sync";
        }

        private void TrayQuit(object sender, RoutedEventArgs e)
        {
            MessageBoxResult answer = MessageBox.Show(
                this,
                "Quit Razer Chroma WLED Studio?\n\nThe strips will stay in their last state.",
                "Quit",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes) return;

            this._quitRequested = true;
            this.Close();
        }
    }
}
