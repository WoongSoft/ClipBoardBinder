using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ID648
{
    internal static class ThemedMessageDialog
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd,int dwAttribute,ref int pvAttribute,int cbAttribute);

        public static void Show(string message,string caption,MessageBoxImage image)
        {
            if(Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => Show(message,caption,image));
                return;
            }

            var dialog = new Window
            {
                Title = caption,
                SizeToContent = SizeToContent.WidthAndHeight,
                MinWidth = 360,
                MaxWidth = 760,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ShowInTaskbar = false,
                Background = Brushes.Transparent
            };

            var owner = Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(window => window.IsActive);
            if(owner != null)
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            dialog.SourceInitialized += (_,_) => ApplyWindowFrameTheme(dialog);

            var rootBorder = new Border
            {
                Padding = new Thickness(18),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1.2)
            };
            rootBorder.SetResourceReference(Control.BackgroundProperty,"WindowBackgroundBrush");
            rootBorder.SetResourceReference(Border.BorderBrushProperty,"ControlBorderBrush");

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var severityText = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0,0,0,10),
                Text = GetSeverityText(image)
            };
            severityText.SetResourceReference(TextBlock.ForegroundProperty,"AccentBrush");
            Grid.SetRow(severityText,0);
            layout.Children.Add(severityText);

            var messageViewer = new ScrollViewer
            {
                MaxHeight = 320,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 22
                }
            };
            ((TextBlock)messageViewer.Content).SetResourceReference(TextBlock.ForegroundProperty,"ControlForegroundBrush");
            Grid.SetRow(messageViewer,1);
            layout.Children.Add(messageViewer);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0,18,0,0)
            };
            Grid.SetRow(buttonPanel,2);

            var okButton = new Button
            {
                Content = Localization.GetString("Button.OK") ?? "OK",
                MinWidth = 96,
                Padding = new Thickness(18,8,18,8),
                IsDefault = true,
                IsCancel = true
            };

            if(Application.Current?.TryFindResource("ProminentActionButtonStyle") is Style prominentButtonStyle)
            {
                okButton.Style = prominentButtonStyle;
            }

            okButton.Click += (_,_) => dialog.DialogResult = true;
            buttonPanel.Children.Add(okButton);
            layout.Children.Add(buttonPanel);

            rootBorder.Child = layout;
            dialog.Content = rootBorder;
            dialog.ShowDialog();
        }

        private static string GetSeverityText(MessageBoxImage image)
        {
            return image switch
            {
                MessageBoxImage.Error => Localization.GetString("Severity.Error"),
                MessageBoxImage.Warning => Localization.GetString("Severity.Warning"),
                _ => Localization.GetString("Severity.Info")
            };
        }

        private static void ApplyWindowFrameTheme(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if(hwnd == IntPtr.Zero)
            {
                return;
            }

            var darkModeValue = IsDarkThemeActive() ? 1 : 0;
            DwmSetWindowAttribute(hwnd,DWMWA_USE_IMMERSIVE_DARK_MODE,ref darkModeValue,sizeof(int));
        }

        private static bool IsDarkThemeActive()
        {
            return Application.Current?.Resources.MergedDictionaries.Any(resourceDictionary =>
            {
                var source = resourceDictionary.Source?.OriginalString;
                return !string.IsNullOrWhiteSpace(source)
                    && source.EndsWith("DarkTheme.xaml",StringComparison.OrdinalIgnoreCase);
            }) == true;
        }
    }
}
