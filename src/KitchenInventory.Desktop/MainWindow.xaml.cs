using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Text.RegularExpressions;

using KitchenInventory.Desktop.ViewModels;

namespace KitchenInventory.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Regex QuantityRegex = new("^\\d*(\\.\\d{0,3})?$");

    public MainWindow(ItemsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += async (_, __) => await vm.LoadAsync();
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Let WPF re-evaluate CanExecute for commands that depend on SelectedItem
        CommandManager.InvalidateRequerySuggested();
    }

    private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !QuantityRegex.IsMatch(proposed);
    }

    private void Quantity_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (e.DataObject.GetDataPresent(DataFormats.Text))
        {
            var pasteText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, pasteText);
            if (!QuantityRegex.IsMatch(proposed))
            {
                e.CancelCommand();
            }
        }
        else
        {
            e.CancelCommand();
        }
    }
}