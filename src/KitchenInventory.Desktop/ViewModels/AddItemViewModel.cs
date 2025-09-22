using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.ViewModels
{
    public class AddItemViewModel : INotifyPropertyChanged, INotifyDataErrorInfo
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly Dictionary<string, List<string>> _errors = new();
        public bool HasErrors => _errors.Values.Any(v => v.Count > 0);
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
        public IEnumerable GetErrors(string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName)) return Array.Empty<string>();
            return _errors.TryGetValue(propertyName!, out var list) ? list : Array.Empty<string>();
        }

        private void SetErrors(string propertyName, IEnumerable<string> errors)
        {
            var hasAny = errors.Any();
            if (hasAny)
            {
                _errors[propertyName] = errors.ToList();
            }
            else
            {
                if (_errors.ContainsKey(propertyName)) _errors.Remove(propertyName);
            }
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }

        private void ValidateProperty(string propertyName)
        {
            switch (propertyName)
            {
                case nameof(ItemName):
                    {
                        var errs = new List<string>();
                        if (string.IsNullOrWhiteSpace(ItemName)) errs.Add("Item name is required.");
                        if (!string.IsNullOrWhiteSpace(ItemName) && ItemName.Length > 200) errs.Add("Item name is too long (max 200).");
                        SetErrors(nameof(ItemName), errs);
                        break;
                    }
                case nameof(Quantity):
                    {
                        var qErrs = new List<string>();
                        if (Quantity < 0m) qErrs.Add("Quantity cannot be negative.");
                        if (Quantity > 1000000m) qErrs.Add("Quantity is unrealistically large.");
                        SetErrors(nameof(Quantity), qErrs);
                        break;
                    }
                case nameof(Unit):
                    {
                        var uErrs = new List<string>();
                        if (!string.IsNullOrEmpty(Unit) && Unit.Length > 32) uErrs.Add("Unit is too long (max 32).");
                        SetErrors(nameof(Unit), uErrs);
                        break;
                    }
                case nameof(SelectedCategoryId):
                    {
                        SetErrors(nameof(SelectedCategoryId), Array.Empty<string>());
                        break;
                    }
                case nameof(ExpiryDate):
                    {
                        SetErrors(nameof(ExpiryDate), Array.Empty<string>());
                        break;
                    }
            }
        }

        public List<Category> Categories { get; }

        private string _itemName = string.Empty;
        public string ItemName
        {
            get => _itemName;
            set
            {
                if (_itemName != value)
                {
                    _itemName = value;
                    OnPropertyChanged();
                    ValidateProperty(nameof(ItemName));
                }
            }
        }

        private decimal _quantity = 1m;
        public decimal Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                    ValidateProperty(nameof(Quantity));
                }
            }
        }

        private string _unit = "pcs";
        public string Unit
        {
            get => _unit;
            set
            {
                if (_unit != value)
                {
                    _unit = value;
                    OnPropertyChanged();
                    ValidateProperty(nameof(Unit));
                }
            }
        }

        private int _selectedCategoryId;
        public int SelectedCategoryId
        {
            get => _selectedCategoryId;
            set
            {
                if (_selectedCategoryId != value)
                {
                    _selectedCategoryId = value;
                    OnPropertyChanged();
                    ValidateProperty(nameof(SelectedCategoryId));
                }
            }
        }

        private DateTime? _expiryDate;
        public DateTime? ExpiryDate
        {
            get => _expiryDate;
            set
            {
                if (_expiryDate != value)
                {
                    _expiryDate = value;
                    OnPropertyChanged();
                    ValidateProperty(nameof(ExpiryDate));
                }
            }
        }

        public AddItemViewModel(List<Category> categories)
        {
            Categories = categories;
            ValidateProperty(nameof(ItemName));
            ValidateProperty(nameof(Quantity));
            ValidateProperty(nameof(Unit));
        }
    }
}