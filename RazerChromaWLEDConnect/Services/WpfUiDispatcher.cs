using System;
using System.Windows.Threading;

namespace RazerChromaWLEDConnect.Services
{
    // The Core project declares IUiDispatcher in RazerChromaWLEDConnect.Services, so this
    // class implements that same interface from the UI layer.
    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        private readonly Dispatcher _dispatcher;

        public WpfUiDispatcher(Dispatcher dispatcher)
        {
            this._dispatcher = dispatcher;
        }

        public bool IsOnUiThread
        {
            get { return this._dispatcher.CheckAccess(); }
        }

        public void Post(Action action)
        {
            if (action == null) return;

            if (this._dispatcher.CheckAccess())
            {
                action();
                return;
            }

            this._dispatcher.BeginInvoke(DispatcherPriority.Background, action);
        }
    }
}
