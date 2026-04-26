using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace ID648
{
    public partial class ThirdPartyNoticesWindow : Window
    {
        public ThirdPartyNoticesWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
