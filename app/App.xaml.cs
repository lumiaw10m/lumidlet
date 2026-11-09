using System.Linq;
using Windows.ApplicationModel.Activation;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Lumidlet
{
    sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            UnhandledException += (s, e) =>
            {
                try
                {
                    System.IO.File.AppendAllText(System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "crash.log"),
                        System.DateTime.Now + " " + e.Exception + "\r\n");
                }
                catch (System.Exception) { }
                e.Handled = true;
            };
        }

        Frame RootFrame()
        {
            var frame = Window.Current.Content as Frame;
            if (frame == null)
            {
                frame = new Frame();
                frame.Navigated += (s, e) =>
                    SystemNavigationManager.GetForCurrentView().AppViewBackButtonVisibility =
                        frame.CanGoBack ? AppViewBackButtonVisibility.Visible : AppViewBackButtonVisibility.Collapsed;
                Window.Current.Content = frame;
                SystemNavigationManager.GetForCurrentView().BackRequested += (s, e) =>
                {
                    if (!e.Handled && frame.CanGoBack && !(frame.Content is GamePage)) { e.Handled = true; frame.GoBack(); }
                };
            }
            return frame;
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            var frame = RootFrame();
            var arg = e.Arguments ?? "";
            if (frame.Content == null) frame.Navigate(typeof(MainPage));
            if (arg.StartsWith("run:"))
            {
                var key = arg.Substring(4);
                string script = null;
                int bar = key.IndexOf('|');
                if (bar >= 0) { script = key.Substring(bar + 1); key = key.Substring(0, bar); }
                var target = AppStore.LoadAll().FirstOrDefault(a => a.Id == key) ??
                             AppStore.LoadAll().FirstOrDefault(a => a.Name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (target != null)
                {
                    if (frame.Content is GamePage) frame.GoBack();
                    frame.Navigate(typeof(GamePage), script == null ? target.Id : target.Id + "|" + script);
                }
            }
            Window.Current.Activate();
        }

        protected override async void OnFileActivated(FileActivatedEventArgs args)
        {
            var frame = RootFrame();
            Window.Current.Activate();
            var main = frame.Content as MainPage;
            if (main != null) { await main.ImportAndMaybeRun(args.Files.ToList()); return; }
            if (frame.Content is GamePage) { MainPage.PendingFiles = args.Files.ToList(); frame.GoBack(); return; }
            MainPage.PendingFiles = args.Files.ToList();
            frame.Navigate(typeof(MainPage));
        }
    }
}
