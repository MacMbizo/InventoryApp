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
using System.Globalization;

namespace KitchenInventory.Desktop;

public partial class MainWindow : Window
{
    private static readonly string DecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
    private static readonly Regex QuantityRegex = new($"^\\d*(?:{Regex.Escape(DecimalSeparator)}\\d{{0,3}})?$");

    public MainWindow(ItemsViewModel viewModel)
    {
        InitializeComponent();
        // If DI constructs this, DataContext is provided via XAML now. If created by DI, keep DI-provided vm.
        if (DataContext is null)
        {
            DataContext = viewModel;
        }
        Loaded += async (_, __) =>
        {
            if (DataContext is ItemsViewModel vm)
            {
                await vm.LoadAsync();
            }
        };
    }

    private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommandManager.InvalidateRequerySuggested();
    }

    private void DataGrid_CurrentCellChanged(object? sender, EventArgs e)
    {
        CommandManager.InvalidateRequerySuggested();
    }

    private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var tb = (TextBox)sender;
        var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
        e.Handled = !QuantityRegex.IsMatch(proposed);
    }

    private void Quantity_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.Text))
        {
            var paste = (string)e.DataObject.GetData(DataFormats.Text)!;
            e.CancelCommand();
            var tb = (TextBox)sender;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, paste);
            if (QuantityRegex.IsMatch(proposed))
            {
                tb.Text = proposed;
                tb.CaretIndex = proposed.Length;
            }
        }
        else
        {
            e.CancelCommand();
        }
    }
}