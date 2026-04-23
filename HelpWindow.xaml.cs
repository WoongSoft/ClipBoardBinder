using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ID648
{
    public partial class HelpWindow : Window
    {
        private const string HomepageUrl = "https://www.woongsoft.com";
        private readonly bool _isFirstRun;
        private bool _isUpdatingSelection;

        public HelpWindow(bool isFirstRun)
        {
            InitializeComponent();
            _isFirstRun = isFirstRun;
            Loaded += HelpWindow_Loaded;
            Closed += HelpWindow_Closed;
            Localization.LanguageChanged += Localization_LanguageChanged;
        }

        private void HelpWindow_Loaded(object sender,RoutedEventArgs e)
        {
            UpdateLanguageSelection();
        }

        private void HelpWindow_Closed(object? sender,EventArgs e)
        {
            Loaded -= HelpWindow_Loaded;
            Closed -= HelpWindow_Closed;
            Localization.LanguageChanged -= Localization_LanguageChanged;
        }

        private void Localization_LanguageChanged()
        {
            if(!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(UpdateLanguageSelection);
                return;
            }

            UpdateLanguageSelection();
        }

        private void UpdateLanguageSelection()
        {
            if(LanguageComboBox == null)
            {
                return;
            }

            _isUpdatingSelection = true;
            try
            {
                var selectedItem = LanguageComboBox.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(item.Tag as string,Localization.CurrentLanguage,StringComparison.OrdinalIgnoreCase));

                LanguageComboBox.SelectedItem = selectedItem ?? LanguageComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }

        private void LanguageComboBox_SelectionChanged(object sender,SelectionChangedEventArgs e)
        {
            if(_isUpdatingSelection)
            {
                return;
            }

            if(LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string languageCode)
            {
                Localization.SetLanguage(languageCode);
            }
        }

        private void ConfirmButton_Click(object sender,RoutedEventArgs e)
        {
            if(_isFirstRun)
            {
                AppSettingsStore.SetWelcomeConfirmed(true);
            }

            DialogResult = true;
        }

        private void CopyButton_Click(object sender,RoutedEventArgs e)
        {
            try
            {
                var text = Localization.GetString("Help.Body");
                if(!string.IsNullOrEmpty(text))
                {
                    Clipboard.SetText(text);
                }
            }
            catch
            {
            }
        }

        private void HomepageLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(HomepageUrl)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                ThemedMessageDialog.Show(Localization.GetString("Link.OpenFailed"), Localization.GetString("App.Title"), MessageBoxImage.Error);
            }
        }
    }
}
