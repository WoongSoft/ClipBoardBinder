using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace ID648
{
    public partial class AboutWindow : Window
    {
        private const string SupportUrl = "https://paypal.me/woongsoft";

        public AboutWindow()
        {
            InitializeComponent();
            Loaded += AboutWindow_Loaded;
        }

        private void AboutWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version?.ToString() ?? string.Empty;
                VersionText.Text = version;

            }
            catch
            {
            }
        }

        private void SupportLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(SupportUrl)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                ThemedMessageDialog.Show(Localization.GetString("Link.OpenFailed"), Localization.GetString("App.Title"), MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
