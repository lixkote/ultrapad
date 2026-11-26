using System;
using Windows.UI.Xaml;

namespace UltraPad.Helpers
{
    // Runs an action at most once per interval. Selection changes come with every mouse move while
    // dragging, and reading the formatting of a long selection each time makes selecting sluggish.
    public sealed class Throttle
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _action;

        public Throttle(TimeSpan interval, Action action)
        {
            _action = action;
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                _action();
            };
        }

        public void Request()
        {
            if (!_timer.IsEnabled) _timer.Start();
        }

        public void RunNow()
        {
            _timer.Stop();
            _action();
        }
    }
}
