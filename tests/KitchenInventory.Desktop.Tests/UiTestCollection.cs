using Xunit;

namespace KitchenInventory.Desktop.Tests;

// Ensures all tests marked [Collection("UI")] run serially and share the same collection
[CollectionDefinition("UI", DisableParallelization = true)]
public class UiTestCollection
{
    // Intentionally empty. This class serves only as an anchor for the collection definition.
}