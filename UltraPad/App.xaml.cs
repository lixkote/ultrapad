using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using UltraPad.Classes;

namespace UltraPad
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    sealed partial class App : Application
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            try
            {
                string theme = ApplicationData.Current.LocalSettings.Values["theme"] as string
                               ?? ApplicationData.Current.LocalSettings.Values["themeSetting"] as string;
                if (theme == "Dark") RequestedTheme = ApplicationTheme.Dark;
                else if (theme == "Light") RequestedTheme = ApplicationTheme.Light;
            }
            catch { }
            InitializeComponent();
            Suspending += OnSuspending;
            UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedException;
            AppDomain.CurrentDomain.FirstChanceException += CurrentDomain_FirstChanceException;
        }

        public static FontClass FClass { get; private set; }

        /// <summary>
        /// Invoked when the application is launched normally by the end user.  Other entry points
        /// will be used such as when the application is launched to open a specific file.
        /// </summary>
        /// <param name="e">Details about the launch request and process.</param>
        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            Frame rootFrame = Window.Current.Content as Frame;

            // Do not repeat app initialization when the Window already has content,
            // just ensure that the window is active
            if (rootFrame == null)
            {
                // Create a Frame to act as the navigation context and navigate to the first page
                rootFrame = new Frame();

                rootFrame.NavigationFailed += OnNavigationFailed;

                if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
                {
                    //TODO: Load state from previously suspended application
                }

                // Place the frame in the current Window
                Window.Current.Content = rootFrame;
            }

            if (e.PrelaunchActivated == false)
            {
                // No CoreApplication.EnablePrelaunch here: every launch starts its own process
                // (SupportsMultipleInstances), and for such apps the call fails with
                // 0x80070490 "Element not found", which crashed UltraPad on start.

                // TODO: This is not a prelaunch activation. Perform operations which
                // assume that the user explicitly launched the app such as updating
                // the online presence of the user on a social network, updating a
                // what's new feed, etc.

                if (rootFrame.Content == null)
                {
                    // When the navigation stack isn't restored navigate to the first page,
                    // configuring the new page by passing required information as a navigation
                    // parameter
                    rootFrame.Navigate(typeof(MainPage), e.Arguments);
                }
                PrepareTitleBar();
                // Ensure the current window is active
                Window.Current.Activate();
            }
        }

        private static void PrepareTitleBar()
        {
            CoreApplication.GetCurrentView().TitleBar.ExtendViewIntoTitleBar = true;
            ApplicationView.GetForCurrentView().TitleBar.ButtonBackgroundColor = Colors.Transparent;
            ApplicationView.GetForCurrentView().TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }

        protected override void OnActivated(IActivatedEventArgs args)
        {
            base.OnActivated(args);

            Frame rootFrame = Window.Current.Content as Frame;

            if (rootFrame == null)
            {
                rootFrame = new Frame();
                rootFrame.NavigationFailed += OnNavigationFailed;
                Window.Current.Content = rootFrame;
            }

            // a page that is already open keeps its document
            if (rootFrame.Content == null) rootFrame.Navigate(typeof(MainPage));
            PrepareTitleBar();
            Window.Current.Activate();
        }

        protected override async void OnFileActivated(FileActivatedEventArgs args)
        {
            var file = args.Files.Count > 0 ? args.Files[0] as StorageFile : null;

            // with SupportsMultipleInstances every file gets its own window; if Windows still hands one to
            // this window, it replaces the document after asking about unsaved changes
            if (Window.Current.Content is Frame current && current.Content is MainPage page)
            {
                Window.Current.Activate();
                if (file != null) await page.OpenFromActivationAsync(file);
                return;
            }

            var frame = new Frame();
            frame.NavigationFailed += OnNavigationFailed;
            frame.Navigate(typeof(MainPage), file);
            Window.Current.Content = frame;
            PrepareTitleBar();
            Window.Current.Activate();
        }

        public static void ApplyAppTheme(string value)
        {
            try
            {
                // "Use system setting" can't be ElementTheme.Default: that falls back to the theme the app
                // was started with (Light or Dark if that was chosen then), not to Windows. So the system
                // theme is read and set explicitly, and followed when Windows switches it.
                bool dark = value == "Dark" || (value != "Light" && SystemUsesDarkTheme());
                RootTheme = dark ? ElementTheme.Dark : ElementTheme.Light;
                WatchSystemTheme();
                var titleBar = Windows.UI.ViewManagement.ApplicationView.GetForCurrentView().TitleBar;
                Windows.UI.Color fg = dark ? Windows.UI.Colors.White : Windows.UI.Colors.Black;
                titleBar.ButtonForegroundColor = fg;
                titleBar.ButtonHoverForegroundColor = fg;
                titleBar.ButtonPressedForegroundColor = fg;
                titleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x09, 0, 0, 0);
                titleBar.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x06, 0, 0, 0);
                titleBar.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(0x5D, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x5C, 0, 0, 0);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ApplyAppTheme: " + ex.Message);
            }
        }

        private static Windows.UI.ViewManagement.UISettings _themeSettings;
        private static readonly System.Collections.Generic.List<Windows.UI.Core.CoreDispatcher> _themeWindows = new System.Collections.Generic.List<Windows.UI.Core.CoreDispatcher>();

        // every window (UltraPad can have several) follows Windows switching between light and dark
        // while the app theme is set to the system setting
        private static void WatchSystemTheme()
        {
            Windows.UI.Core.CoreDispatcher dispatcher = Window.Current?.Dispatcher;
            lock (_themeWindows)
            {
                if (dispatcher != null && !_themeWindows.Contains(dispatcher)) _themeWindows.Add(dispatcher);
                if (_themeSettings != null) return;
                _themeSettings = new Windows.UI.ViewManagement.UISettings();
            }
            _themeSettings.ColorValuesChanged += (sender, args) =>
            {
                Windows.UI.Core.CoreDispatcher[] windows;
                lock (_themeWindows) windows = _themeWindows.ToArray();
                foreach (Windows.UI.Core.CoreDispatcher window in windows)
                {
                    try
                    {
                    _ = window.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
                    {
                        string setting = ApplicationData.Current.LocalSettings.Values["theme"] as string
                                         ?? ApplicationData.Current.LocalSettings.Values["themeSetting"] as string
                                         ?? "System";
                        if (setting != "Dark" && setting != "Light") ApplyAppTheme(setting);
                    });
                    }
                    catch { } // that window has been closed
                }
            };
        }

        public static bool SystemUsesDarkTheme()
        {
            try
            {
                Color bg = new UISettings().GetColorValue(UIColorType.Background);
                return bg.R + bg.G + bg.B < 382;
            }
            catch { return Current.RequestedTheme == ApplicationTheme.Dark; }
        }

        public static ElementTheme RootTheme
        {
            get
            {
                if (Window.Current.Content is FrameworkElement rootElement)
                {
                    return rootElement.RequestedTheme;
                }
                return ElementTheme.Default;
            }
            set
            {
                if (Window.Current.Content is FrameworkElement rootElement)
                {
                    rootElement.RequestedTheme = value;
                }
            }
        }

        public static TEnum GetEnum<TEnum>(string text) where TEnum : struct
        {
            if (!typeof(TEnum).GetTypeInfo().IsEnum)
            {
                throw new InvalidOperationException("Generic parameter 'TEnum' must be an enum.");
            }
            return (TEnum)Enum.Parse(typeof(TEnum), text);
        }

        /// <summary>
        /// Invoked when Navigation to a certain page fails
        /// </summary>
        /// <param name="sender">The Frame which failed navigation</param>
        /// <param name="e">Details about the navigation failure</param>
        void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
        }

        /// <summary>
        /// Invoked when application execution is being suspended.  Application state is saved
        /// without knowing whether the application will be terminated or resumed with the contents
        /// of memory still intact.
        /// </summary>
        /// <param name="sender">The source of the suspend request.</param>
        /// <param name="e">Details about the suspend request.</param>
        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            //TODO: Save application state and stop any background activity

            deferral.Complete();
        }

        #region Error handling
        private static void OnUnobservedException(object sender, UnobservedTaskExceptionEventArgs e) => e.SetObserved();

        private static void OnUnhandledException(object sender, Windows.UI.Xaml.UnhandledExceptionEventArgs e) => e.Handled = true;

        private void CurrentDomain_FirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
        }
        #endregion
    }
}