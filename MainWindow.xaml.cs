using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;


namespace ID648
{
    public partial class MainWindow : Window
    {
        private const string PrivacyPolicyUrl = "https://www.woongsoft.com/privacy-policy/";
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int WM_NCACTIVATE = 0x0086;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private static readonly Uri DarkThemeDictionaryUri = new("DarkTheme.xaml",UriKind.Relative);
        private static readonly Uri LightThemeDictionaryUri = new("LightTheme.xaml",UriKind.Relative);

        [DllImport("user32.dll",SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll",SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd,int dwAttribute,ref int pvAttribute,int cbAttribute);

        private MainViewModel ViewModel => DataContext as MainViewModel;
        private readonly DispatcherTimer _clipboardUpdateTimer;
        private HwndSource? _hwndSource;
        private ResourceDictionary? _currentThemeDictionary;
        private bool _isClipboardListenerRegistered;
        private bool _isDisposed;

        public MainWindow(MainViewModel mainViewModel)
        {
            InitializeComponent();
            _clipboardUpdateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _clipboardUpdateTimer.Tick += ClipboardUpdateTimer_Tick;
            _currentThemeDictionary = GetCurrentThemeDictionary();
            DataContext = mainViewModel;
            if(ViewModel != null)
            {
                ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            SourceInitialized += MainWindow_SourceInitialized;
            Closed += MainWindow_Closed;
            Activated += MainWindow_ActivationChanged;
            Deactivated += MainWindow_ActivationChanged;
            StateChanged += MainWindow_ActivationChanged;
            Localization.LanguageChanged += Localization_LanguageChanged;
        }

        private void MainWindow_SourceInitialized(object? sender,EventArgs e)
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if(_hwndSource == null)
            {
                return;
            }
            _hwndSource.AddHook(WndProc);
            _isClipboardListenerRegistered = AddClipboardFormatListener(_hwndSource.Handle);
            ApplyTheme();
            UpdateLocalizedUiState();
            UpdateMaximizeRestoreButtonIcon();
        }

        private void Localization_LanguageChanged()
        {
            if(_isDisposed)
            {
                return;
            }

            Dispatcher.Invoke(UpdateLocalizedUiState);
        }

        private void UpdateLocalizedUiState()
        {
            if(Application.Current?.TryFindResource("App.Title") is string title)
            {
                Title = title;
            }

            if(SystemLanguageMenuItem != null)
            {
                SystemLanguageMenuItem.IsChecked = Localization.IsLanguageSelected("system");
            }

            if(EnglishLanguageMenuItem != null)
            {
                EnglishLanguageMenuItem.IsChecked = Localization.IsLanguageSelected("en");
            }

            if(KoreanLanguageMenuItem != null)
            {
                KoreanLanguageMenuItem.IsChecked = Localization.IsLanguageSelected("ko");
            }

            if(JapaneseLanguageMenuItem != null)
            {
                JapaneseLanguageMenuItem.IsChecked = Localization.IsLanguageSelected("ja");
            }

            if(SpanishLanguageMenuItem != null)
            {
                SpanishLanguageMenuItem.IsChecked = Localization.IsLanguageSelected("es");
            }
        }

        private void MainWindow_Closed(object? sender,EventArgs e)
        {
            if(_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _clipboardUpdateTimer.Stop();
            _clipboardUpdateTimer.Tick -= ClipboardUpdateTimer_Tick;

            if(_hwndSource != null)
            {
                if(_isClipboardListenerRegistered)
                {
                    RemoveClipboardFormatListener(_hwndSource.Handle);
                    _isClipboardListenerRegistered = false;
                }

                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }

            if(ViewModel != null)
            {
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
                ViewModel.Dispose();
            }

            SourceInitialized -= MainWindow_SourceInitialized;
            Closed -= MainWindow_Closed;
            Activated -= MainWindow_ActivationChanged;
            Deactivated -= MainWindow_ActivationChanged;
            StateChanged -= MainWindow_ActivationChanged;
            Localization.LanguageChanged -= Localization_LanguageChanged;
        }

        private async void ClipboardUpdateTimer_Tick(object? sender,EventArgs e)
        {
            _clipboardUpdateTimer.Stop();

            if(_isDisposed || ViewModel == null)
            {
                return;
            }

            try
            {
                await ViewModel.UpdateClipboardContentAsync();
            }
            catch(ObjectDisposedException)
            {
            }
        }

        private void MainWindow_ActivationChanged(object? sender,EventArgs e)
        {
            ApplyTheme();
            UpdateMaximizeRestoreButtonIcon();
        }

        private void ViewModel_PropertyChanged(object? sender,PropertyChangedEventArgs e)
        {
            if(e.PropertyName == nameof(MainViewModel.SelectedTheme))
            {
                ApplyTheme();
            }
        }

        private void ApplyTheme()
        {
            var isDarkMode = ViewModel?.SelectedTheme != AppTheme.Light;
            SetCurrentThemeDictionary(isDarkMode ? DarkThemeDictionaryUri : LightThemeDictionaryUri);
            ApplyWindowFrameTheme(isDarkMode);
        }

        private ResourceDictionary? GetCurrentThemeDictionary()
        {
            return Application.Current.Resources.MergedDictionaries.FirstOrDefault(IsThemeDictionary);
        }

        private void SetCurrentThemeDictionary(Uri themeDictionaryUri)
        {
            var mergedDictionaries = Application.Current.Resources.MergedDictionaries;
            if(_currentThemeDictionary != null
                && _currentThemeDictionary.Source != null
                && _currentThemeDictionary.Source.OriginalString.EndsWith(themeDictionaryUri.OriginalString,StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if(_currentThemeDictionary != null)
            {
                mergedDictionaries.Remove(_currentThemeDictionary);
            }

            _currentThemeDictionary = new ResourceDictionary { Source = themeDictionaryUri };
            mergedDictionaries.Add(_currentThemeDictionary);
        }

        private static bool IsThemeDictionary(ResourceDictionary resourceDictionary)
        {
            var source = resourceDictionary.Source?.OriginalString;
            if(string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            return source.EndsWith(DarkThemeDictionaryUri.OriginalString,StringComparison.OrdinalIgnoreCase)
                || source.EndsWith(LightThemeDictionaryUri.OriginalString,StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyWindowFrameTheme(bool isDarkMode)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if(hwnd == IntPtr.Zero)
            {
                return;
            }

            var darkModeValue = isDarkMode ? 1 : 0;
            DwmSetWindowAttribute(hwnd,DWMWA_USE_IMMERSIVE_DARK_MODE,ref darkModeValue,sizeof(int));
        }

        private IntPtr WndProc(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
        {
            if(msg == WM_CLIPBOARDUPDATE)
            {
                if(!_isDisposed)
                {
                    _clipboardUpdateTimer.Stop();
                    _clipboardUpdateTimer.Start();
                }

                handled = true;
            }

            if(msg == WM_NCACTIVATE)
            {
                ApplyTheme();
            }

            return IntPtr.Zero;
        }

        private void TitleBar_MouseLeftButtonDown(object sender,MouseButtonEventArgs e)
        {
            if(e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if(e.ClickCount == 2)
            {
                ToggleMaximizeRestore();
                return;
            }

            if(WindowState == WindowState.Normal)
            {
                DragMove();
            }
        }

        private void TitleBar_MouseRightButtonUp(object sender,MouseButtonEventArgs e)
        {
            SystemCommands.ShowSystemMenu(this,PointToScreen(e.GetPosition(this)));
        }

        private void MinimizeButton_Click(object sender,RoutedEventArgs e)
        {
            SystemCommands.MinimizeWindow(this);
        }

        private void CloseButton_Click(object sender,RoutedEventArgs e)
        {
            SystemCommands.CloseWindow(this);
        }

        private void MaximizeRestoreButton_Click(object sender,RoutedEventArgs e)
        {
            ToggleMaximizeRestore();
        }

        private void RemoveSelectedClipboardItemsButton_Click(object sender,RoutedEventArgs e)
        {
            if(ViewModel == null || TrackedFilesListBox.SelectedItems.Count == 0)
            {
                return;
            }

            var selectedFilePaths = TrackedFilesListBox.SelectedItems
                .OfType<TrackedFileItem>()
                .Select(item => item.FilePath)
                .ToArray();

            ViewModel.RemoveSelectedFileClipboardItems(selectedFilePaths);
        }

        private void ToggleMaximizeRestore()
        {
            if(ResizeMode == ResizeMode.NoResize)
            {
                return;
            }

            if(WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
            }

            UpdateMaximizeRestoreButtonIcon();
        }

        private void UpdateMaximizeRestoreButtonIcon()
        {
            if(MaximizeRestoreButton == null)
            {
                return;
            }

            MaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void LanguageMenuItem_Click(object sender,RoutedEventArgs e)
        {
            if(sender is System.Windows.Controls.MenuItem mi && mi.Tag is string tag)
            {
                Localization.SetLanguage(tag);
            }
        }

        public void ShowWelcomeIfNeeded()
        {
            if(_isDisposed || AppSettingsStore.WelcomeConfirmed)
            {
                return;
            }

            ShowHelpDialog(true);
        }

        private void HelpMenuItem_Click(object sender,RoutedEventArgs e)
        {
            ShowHelpDialog(false);
        }

        private void HomepageMenuItem_Click(object sender,RoutedEventArgs e)
        {
            try
            {
                if(!NetworkInterface.GetIsNetworkAvailable())
                {
                    ThemedMessageDialog.Show(Localization.GetString("Homepage.NoNetwork"),Localization.GetString("App.Title"),MessageBoxImage.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo("https://www.woongsoft.com")
                {
                    UseShellExecute = true
                });
            }
            catch(Exception)
            {
                ThemedMessageDialog.Show(Localization.GetString("Homepage.OpenFailed"),Localization.GetString("App.Title"),MessageBoxImage.Error);
            }
        }

        private void AboutMenuItem_Click(object sender,RoutedEventArgs e)
        {
            try
            {
                var about = new AboutWindow()
                {
                    Owner = this
                };

                about.ShowDialog();
            }
            catch(Exception)
            {
                ThemedMessageDialog.Show(Localization.GetString("App.UnexpectedError"),Localization.GetString("App.Title"),MessageBoxImage.Error);
            }
        }

        private void ShowHelpDialog(bool isFirstRun)
        {
            var helpWindow = new HelpWindow(isFirstRun)
            {
                Owner = this
            };

            helpWindow.ShowDialog();
        }

        private void ThirdPartyNoticesMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var tpWindow = new ThirdPartyNoticesWindow()
                {
                    Owner = this
                };
                tpWindow.ShowDialog();
            }
            catch (Exception)
            {
                ThemedMessageDialog.Show("Failed to open third-party notices window.", "Error", MessageBoxImage.Error);
            }
        }

        private void PrivacyPolicyMenuItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!NetworkInterface.GetIsNetworkAvailable())
                {
                    ThemedMessageDialog.Show(Localization.GetString("Homepage.NoNetwork"), Localization.GetString("App.Title"), MessageBoxImage.Warning);
                    return;
                }
                Process.Start(new ProcessStartInfo(PrivacyPolicyUrl)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                ThemedMessageDialog.Show(Localization.GetString("Link.OpenFailed"), Localization.GetString("App.Title"), MessageBoxImage.Error);
            }
        }
    }
}
