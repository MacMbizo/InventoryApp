using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using KitchenInventory.Desktop.Services;

namespace KitchenInventory.Desktop.ViewModels
{
    public class UnitConverterViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public IReadOnlyList<string> Units { get; } = UnitConversionService.KnownUnits;

        private string _inputText = "1";
        public string InputText
        {
            get => _inputText;
            set { if (_inputText != value) { _inputText = value; OnPropertyChanged(); Recompute(); } }
        }

        private string _fromUnit = "kg";
        public string FromUnit
        {
            get => _fromUnit;
            set { if (_fromUnit != value) { _fromUnit = value; OnPropertyChanged(); Recompute(); } }
        }

        private string _toUnit = "g";
        public string ToUnit
        {
            get => _toUnit;
            set { if (_toUnit != value) { _toUnit = value; OnPropertyChanged(); Recompute(); } }
        }

        private string _resultText = string.Empty;
        public string ResultText
        {
            get => _resultText;
            private set { if (_resultText != value) { _resultText = value; OnPropertyChanged(); } }
        }

        public UnitConverterViewModel()
        {
            Recompute();
        }

        public void Swap()
        {
            var from = FromUnit;
            FromUnit = ToUnit;
            ToUnit = from;
        }

        private void Recompute()
        {
            var raw = (InputText ?? string.Empty).Trim();
            if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) &&
                !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            {
                ResultText = "Enter a valid number.";
                return;
            }

            ResultText = UnitConversionService.FormatConversion(value, FromUnit, ToUnit);
        }
    }
}
