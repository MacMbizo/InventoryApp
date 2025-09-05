namespace KitchenInventory.Desktop.Constants;

/// <summary>
/// Standardized exit codes for the Kitchen Inventory application.
/// </summary>
public static class ExitCodes
{
    /// <summary>
    /// Success - the application completed successfully.
    /// </summary>
    public const int Success = 0;
    
    /// <summary>
    /// General error - unspecified failure condition.
    /// </summary>
    public const int GeneralError = 1;
    
    /// <summary>
    /// Invalid arguments - incorrect command line parameters provided.
    /// </summary>
    public const int InvalidArguments = 2;
    
    /// <summary>
    /// Database error - failure to connect to or operate on the database.
    /// </summary>
    public const int DatabaseError = 10;
    
    /// <summary>
    /// File operation error - failed to read, write, or access files.
    /// </summary>
    public const int FileOperationError = 11;
    
    /// <summary>
    /// Export error - diagnostics export operation failed.
    /// </summary>
    public const int ExportError = 20;
    
    /// <summary>
    /// Configuration error - invalid or missing configuration.
    /// </summary>
    public const int ConfigurationError = 30;
}