using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KitchenInventory.Desktop.Services
{
    /// <summary>
    /// Pure, dependency-free unit conversion catalog.
    /// Supports mass (mg/g/kg) and volume (ml/cl/dl/l) conversions.
    /// </summary>
    public static class UnitConversionService
    {
        private sealed record UnitDef(string Canonical, string Dimension, decimal FactorToBase);

        // Canonical units keyed by lowercase alias. Base units: gram (mass), millilitre (volume).
        private static readonly Dictionary<string, UnitDef> AliasMap = BuildAliasMap();

        private static readonly string[] CanonicalUnitOrder =
        {
            "mg", "g", "kg",
            "ml", "cl", "dl", "l"
        };

        private static Dictionary<string, UnitDef> BuildAliasMap()
        {
            var map = new Dictionary<string, UnitDef>(StringComparer.OrdinalIgnoreCase);

            var defs = new[]
            {
                new UnitDef("g",  "mass",   1m),
                new UnitDef("kg", "mass",   1000m),
                new UnitDef("mg", "mass",   0.001m),
                new UnitDef("ml", "volume", 1m),
                new UnitDef("cl", "volume", 10m),
                new UnitDef("dl", "volume", 100m),
                new UnitDef("l",  "volume", 1000m),
            };

            var aliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["g"]  = new[] { "g", "gm", "gram", "grams" },
                ["kg"] = new[] { "kg", "kilo", "kilos", "kilogram", "kilograms" },
                ["mg"] = new[] { "mg", "milligram", "milligrams" },
                ["ml"] = new[] { "ml", "milliliter", "milliliters", "millilitre", "millilitres" },
                ["cl"] = new[] { "cl", "centiliter", "centiliters", "centilitre", "centilitres" },
                ["dl"] = new[] { "dl", "deciliter", "deciliters", "decilitre", "decilitres" },
                ["l"]  = new[] { "l", "liter", "liters", "litre", "litres" },
            };

            foreach (var def in defs)
            {
                foreach (var alias in aliases[def.Canonical])
                {
                    map[alias] = def;
                }
            }
            return map;
        }

        /// <summary>Canonical unit symbols exposed to the UI, in a stable order.</summary>
        public static IReadOnlyList<string> KnownUnits { get; } = CanonicalUnitOrder;

        /// <summary>Total number of distinct unit aliases understood by the catalog.</summary>
        public static int AliasCount => AliasMap.Count;

        /// <summary>
        /// Resolves a (possibly aliased) unit string to its canonical symbol, e.g. "Kilograms" -> "kg".
        /// </summary>
        public static bool TryGetCanonical(string? unit, out string canonical)
        {
            canonical = string.Empty;
            if (string.IsNullOrWhiteSpace(unit)) return false;
            if (AliasMap.TryGetValue(unit.Trim(), out var def))
            {
                canonical = def.Canonical;
                return true;
            }
            return false;
        }

        /// <summary>True when both units are known and belong to the same dimension (mass or volume).</summary>
        public static bool AreCompatible(string? fromUnit, string? toUnit)
            => TryGetCanonical(fromUnit, out var f)
               && TryGetCanonical(toUnit, out var t)
               && string.Equals(AliasMap[f].Dimension, AliasMap[t].Dimension, StringComparison.Ordinal);

        /// <summary>
        /// Converts <paramref name="value"/> from one unit to another.
        /// Returns false when either unit is unknown or the units are dimensionally incompatible.
        /// </summary>
        public static bool TryConvert(decimal value, string? fromUnit, string? toUnit, out decimal result)
        {
            result = 0m;
            if (!TryGetCanonical(fromUnit, out var fromCanonical)) return false;
            if (!TryGetCanonical(toUnit, out var toCanonical)) return false;

            var from = AliasMap[fromCanonical];
            var to = AliasMap[toCanonical];
            if (!string.Equals(from.Dimension, to.Dimension, StringComparison.Ordinal)) return false;

            var converted = value * from.FactorToBase / to.FactorToBase;
            result = Math.Round(converted, 6, MidpointRounding.AwayFromZero);
            return true;
        }

        /// <summary>
        /// Formats a conversion for display, e.g. "1.5 kg = 1500 g".
        /// </summary>
        public static string FormatConversion(decimal value, string? fromUnit, string? toUnit)
        {
            var valueText = value.ToString("0.######", CultureInfo.InvariantCulture);
            if (TryConvert(value, fromUnit, toUnit, out var result))
            {
                var resultText = result.ToString("0.######", CultureInfo.InvariantCulture);
                return $"{valueText} {fromUnit} = {resultText} {toUnit}";
            }
            return $"Cannot convert {valueText} {fromUnit} to {toUnit}.";
        }

        /// <summary>Returns the canonical units belonging to a dimension ("mass" or "volume").</summary>
        public static IReadOnlyList<string> UnitsForDimension(string dimension)
            => CanonicalUnitOrder
                .Where(u => string.Equals(AliasMap[u].Dimension, dimension, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }
}
