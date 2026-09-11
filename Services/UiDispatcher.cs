using Avalonia.Threading;
using System;

namespace Circle_Tracker.Services;

internal static class UiDispatcher
{
    internal static void Post(Action action)
    {
        try
        {
            Dispatcher? dispatcher = Dispatcher.UIThread;

            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            dispatcher.Post(action);
        }
        catch
        {
            action();
        }
    }
}
