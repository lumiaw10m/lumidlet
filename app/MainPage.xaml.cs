using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace Lumidlet
{
    public sealed class AppItem
    {
        public AppEntry Entry { get; set; }
        public string Name { get; set; }
        public string Sub { get; set; }
        public BitmapImage Icon { get; set; }
    }

    public sealed partial class MainPage : Page
    {
        public static IReadOnlyList<IStorageItem> PendingFiles;

        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = NavigationCacheMode.Disabled;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ImportInbox();
            Refresh();
            var files = PendingFiles;
            PendingFiles = null;
            if (files != null && files.Count > 0) await ImportAndMaybeRun(files);
        }

        public async Task ImportAndMaybeRun(IReadOnlyList<IStorageItem> files)
        {
            var added = await Import(files);
            if (added.Count == 1) Frame.Navigate(typeof(GamePage), added[0].Id);
        }

        async Task ImportInbox()
        {
            try
            {
                var local = ApplicationData.Current.LocalFolder;
                var inbox = await local.CreateFolderAsync("inbox", CreationCollisionOption.OpenIfExists);
                var files = (await inbox.GetFilesAsync()).Where(f => f.FileType.Equals(".jar", StringComparison.OrdinalIgnoreCase) || f.FileType.Equals(".jad", StringComparison.OrdinalIgnoreCase)).ToList();
                if (files.Count == 0) return;
                await Import(files.Cast<IStorageItem>().ToList());
                foreach (var f in files) await f.DeleteAsync();
            }
            catch (Exception) { }
        }

        void Refresh()
        {
            var items = AppStore.LoadAll().Select(a => new AppItem
            {
                Entry = a,
                Name = a.Name,
                Sub = a.Subtitle,
                Icon = new BitmapImage(new Uri(a.IconUri)),
            }).ToList();
            List.ItemsSource = items;
            Empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        async Task<List<AppEntry>> Import(IReadOnlyList<IStorageItem> files)
        {
            Busy.IsActive = true;
            var added = new List<AppEntry>();
            try { added = await AppStore.Import(files); }
            catch (Exception ex) { await new MessageDialog("Could not import: " + ex.Message, "Lumidlet").ShowAsync(); }
            finally { Busy.IsActive = false; }
            Refresh();
            return added;
        }

        async void Add_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.Downloads };
            picker.FileTypeFilter.Add(".jar");
            picker.FileTypeFilter.Add(".jad");
            var files = await picker.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;
            await Import(files.Cast<IStorageItem>().ToList());
        }

        async void About_Click(object sender, RoutedEventArgs e)
        {
            await new MessageDialog(
                "Lumidlet runs Java ME (CLDC 1.1 / MIDP 2.0) games and apps, including the Nokia UI API, MMAPI sound, RMS saves and HTTP.\n\n" +
                "Press and hold a game to change its screen size, sound and keypad. In a game, the Back button opens the menu.\n\n" +
                "Keyboard: arrows, Enter = fire, F1/F2 = soft keys, digits, E = *, R = #.",
                "Lumidlet").ShowAsync();
        }

        void List_ItemClick(object sender, ItemClickEventArgs e)
        {
            var item = e.ClickedItem as AppItem;
            if (item != null) Frame.Navigate(typeof(GamePage), item.Entry.Id);
        }

        void Item_Holding(object sender, HoldingRoutedEventArgs e)
        {
            if (e.HoldingState != Windows.UI.Input.HoldingState.Started) return;
            e.Handled = true;
            Settings(((FrameworkElement)sender).DataContext as AppItem);
        }

        void Item_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (e.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Touch) return;
            e.Handled = true;
            Settings(((FrameworkElement)sender).DataContext as AppItem);
        }

        bool settingsOpen;

        async void Settings(AppItem item)
        {
            if (item == null || settingsOpen) return;
            settingsOpen = true;
            try
            {
                var a = item.Entry;
                var panel = new StackPanel();
                var size = new ComboBox { Header = "Screen size", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
                int sel = -1;
                for (int i = 0; i < AppStore.Sizes.Length; i++)
                {
                    size.Items.Add(AppStore.Sizes[i][0] + " × " + AppStore.Sizes[i][1]);
                    if (AppStore.Sizes[i][0] == a.Width && AppStore.Sizes[i][1] == a.Height) sel = i;
                }
                if (sel < 0) { size.Items.Add(a.Width + " × " + a.Height); sel = size.Items.Count - 1; }
                size.SelectedIndex = sel;
                panel.Children.Add(size);
                ComboBox midlet = null;
                if (a.Midlets.Count > 1)
                {
                    midlet = new ComboBox { Header = "MIDlet", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
                    foreach (var m in a.Midlets) midlet.Items.Add(m);
                    midlet.SelectedIndex = Math.Max(0, Math.Min(a.Midlets.Count - 1, a.Midlet - 1));
                    panel.Children.Add(midlet);
                }
                var enc = new ComboBox { Header = "Text encoding", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var x in new[] { "ISO-8859-1", "UTF-8", "windows-1251" }) enc.Items.Add(x);
                enc.SelectedItem = enc.Items.Contains(a.Encoding) ? a.Encoding : "ISO-8859-1";
                panel.Children.Add(enc);
                var sound = new ToggleSwitch { Header = "Sound", IsOn = a.Sound };
                var keypad = new ToggleSwitch { Header = "On-screen keypad", IsOn = a.Keypad };
                var smooth = new ToggleSwitch { Header = "Smooth scaling", IsOn = a.Smooth };
                var rotate = new ToggleSwitch { Header = "Landscape", IsOn = a.Rotate };
                panel.Children.Add(sound);
                panel.Children.Add(keypad);
                var layout = new ComboBox { Header = "Keypad layout", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8) };
                foreach (var x in Keypad.LayoutNames) layout.Items.Add(x);
                layout.SelectedIndex = a.PadLayout;
                panel.Children.Add(layout);
                var diag = new ToggleSwitch { Header = "Diagonals press two arrows (split layout)", IsOn = a.DiagCombo };
                panel.Children.Add(diag);
                panel.Children.Add(smooth);
                panel.Children.Add(rotate);
                var pin = new Button { Content = "Pin to Start", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
                pin.Click += async (s2, e2) =>
                {
                    try
                    {
                        var tile = new Windows.UI.StartScreen.SecondaryTile("g" + a.Id, a.Name, "run:" + a.Id,
                            new Uri("ms-appx:///Assets/Square150x150Logo.png"), Windows.UI.StartScreen.TileSize.Square150x150);
                        tile.VisualElements.ShowNameOnSquare150x150Logo = true;
                        tile.VisualElements.BackgroundColor = Windows.UI.Colors.Transparent;
                        if (a.Icon != null) tile.VisualElements.Square71x71Logo = new Uri(a.IconUri);
                        await tile.RequestCreateAsync();
                    }
                    catch (Exception) { }
                };
                panel.Children.Add(pin);
                var dlg = new ContentDialog
                {
                    Title = a.Name,
                    Content = new ScrollViewer { Content = panel, MaxHeight = 420 },
                    PrimaryButtonText = "Save",
                    SecondaryButtonText = "Delete",
                    IsSecondaryButtonEnabled = true,
                };
                dlg.CloseButtonText = "Cancel";
                var res = await dlg.ShowAsync();
                if (res == ContentDialogResult.Primary)
                {
                    int i = size.SelectedIndex;
                    if (i >= 0 && i < AppStore.Sizes.Length) { a.Width = AppStore.Sizes[i][0]; a.Height = AppStore.Sizes[i][1]; }
                    if (midlet != null) a.Midlet = midlet.SelectedIndex + 1;
                    a.Encoding = (string)enc.SelectedItem;
                    a.Sound = sound.IsOn; a.Keypad = keypad.IsOn; a.PadLayout = Math.Max(0, layout.SelectedIndex); a.DiagCombo = diag.IsOn; a.Smooth = smooth.IsOn; a.Rotate = rotate.IsOn;
                    AppStore.Save(a);
                    Refresh();
                }
                else if (res == ContentDialogResult.Secondary)
                {
                    var confirm = new MessageDialog("Delete " + a.Name + " and its saved data? This can't be undone.", "Delete");
                    confirm.Commands.Add(new UICommand("delete"));
                    confirm.Commands.Add(new UICommand("cancel"));
                    confirm.DefaultCommandIndex = 1;
                    confirm.CancelCommandIndex = 1;
                    var c = await confirm.ShowAsync();
                    if (c.Label == "delete") { AppStore.Delete(a); Refresh(); }
                }
            }
            finally { settingsOpen = false; }
        }
    }
}
