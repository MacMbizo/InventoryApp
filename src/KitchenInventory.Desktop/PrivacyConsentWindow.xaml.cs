using System.Windows;

namespace KitchenInventory.Desktop
{
    public partial class PrivacyConsentWindow : Window
    {
        public bool EnableReporting { get; private set; }

        public PrivacyConsentWindow()
        {
            InitializeComponent();
        }

        private void Enable_Click(object sender, RoutedEventArgs e)
        {
            EnableReporting = true;
            DialogResult = true;
        }

        private void NoThanks_Click(object sender, RoutedEventArgs e)
        {
            EnableReporting = false;
            DialogResult = false;
        }
    }
}