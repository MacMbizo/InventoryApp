using System.Windows;
using KitchenInventory.Desktop.ViewModels;

namespace KitchenInventory.Desktop
{
    public partial class UnitConverterDialog : Window
    {
        public UnitConverterViewModel Vm { get; }

        public UnitConverterDialog()
        {
            InitializeComponent();
            Vm = new UnitConverterViewModel();
            DataContext = Vm;
        }

        private void OnSwapClick(object sender, RoutedEventArgs e) => Vm.Swap();
    }
}
