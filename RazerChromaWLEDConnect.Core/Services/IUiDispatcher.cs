using System;

namespace RazerChromaWLEDConnect.Services
{
    /// <summary>
    /// Lets the view models hand work back to the UI thread without referencing WPF. Device
    /// property changes arrive from the thread pool; touching an ObservableCollection from
    /// there is what used to tear the window down.
    /// </summary>
    public interface IUiDispatcher
    {
        bool IsOnUiThread { get; }

        void Post(Action action);
    }
}
