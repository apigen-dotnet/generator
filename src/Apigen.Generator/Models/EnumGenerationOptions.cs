namespace Apigen.Generator.Models;

/// <summary>
/// Configuration for enhanced enum generation
/// </summary>
public class EnumGenerationOptions
{
  /// <summary>
  /// Enum generation strategy
  /// </summary>
  public EnumGenerationMode GenerationMode { get; set; } = EnumGenerationMode.Smart;

  /// <summary>
  /// How to handle unknown enum values
  /// </summary>
  public UnknownValueHandling UnknownValueHandling { get; set; } = UnknownValueHandling.UseDefault;

  /// <summary>
  /// Whether to add Description attributes to enum members
  /// </summary>
  public bool AddDescriptionAttributes { get; set; } = true;

  /// <summary>
  /// Whether to add JsonStringEnumConverter to enum types
  /// </summary>
  public bool AddJsonStringEnumConverter { get; set; } = true;

  /// <summary>
  /// Whether to add JsonStringEnumMemberName attributes to enum members (.NET 9+)
  /// </summary>
  public bool AddJsonStringEnumMemberName { get; set; } = false;

  /// <summary>
  /// Name generation rules
  /// </summary>
  public EnumNameGenerationOptions NameGeneration { get; set; } = new();
}

/// <summary>
/// Enum generation strategies
/// </summary>
public enum EnumGenerationMode
{
  /// <summary>
  /// Generate only raw enums (e.g., _1, _2, _3) - guaranteed compatibility
  /// </summary>
  Raw,

  /// <summary>
  /// Generate enhanced enums when possible (e.g., BankTransfer, Cash) - fallback to raw
  /// </summary>
  Enhanced,

  /// <summary>
  /// Smart generation - uses best strategy based on available metadata
  /// </summary>
  Smart,

  /// <summary>
  /// Generate both raw and enhanced versions for maximum compatibility
  /// </summary>
  Dual,
}

/// <summary>
/// How to handle unknown enum values during deserialization
/// </summary>
public enum UnknownValueHandling
{
  /// <summary>
  /// Use the default enum value (typically first or Unknown if available)
  /// </summary>
  UseDefault,

  /// <summary>
  /// Return null for nullable enums
  /// </summary>
  UseNull,

  /// <summary>
  /// Throw an exception
  /// </summary>
  Throw,
}

/// <summary>
/// Configuration for generating enum member names
/// </summary>
public class EnumNameGenerationOptions
{
  /// <summary>
  /// Words to remove from descriptions when generating names
  /// </summary>
  public List<string> RemoveWords { get; set; } = new() {"Type", "Types"};

  /// <summary>
  /// Naming convention to use
  /// </summary>
  public string Casing { get; set; } = "PascalCase";

  /// <summary>
  /// Maximum length for generated names
  /// </summary>
  public int MaxLength { get; set; } = 50;

  /// <summary>
  /// How to handle numeric enum values
  /// </summary>
  public string HandleNumbers { get; set; } = "prefix_underscore";

  /// <summary>
  /// Character to replace invalid characters with
  /// </summary>
  public string InvalidCharReplacement { get; set; } = "";

  /// <summary>
  /// Whether to preserve known acronyms in uppercase
  /// </summary>
  public bool PreserveAcronyms { get; set; } = true;
}

/// <summary>
/// Represents an enhanced enum with both raw and friendly values
/// </summary>
public class EnhancedEnumInfo
{
  public string Name { get; set; } = string.Empty;
  public string Description { get; set; } = string.Empty;
  public Dictionary<string, EnumMemberInfo> Members { get; set; } = new();
  public EnumGenerationStrategy Strategy { get; set; }
  public bool HasDescriptions { get; set; }
  public bool IsNumeric { get; set; }
}

/// <summary>
/// Information about an enum member
/// </summary>
public class EnumMemberInfo
{
  public string RawValue { get; set; } = string.Empty;
  public string EnhancedName { get; set; } = string.Empty;
  public string? Description { get; set; }
  public int? NumericValue { get; set; }
}

/// <summary>
/// Strategy used for generating a specific enum
/// </summary>
public enum EnumGenerationStrategy
{
  PreserveValues, // Values are already developer-friendly
  UseDescriptions, // Use x-enumDescriptions
  ParseDescription, // Parse description field for mappings
  NumericWithContext, // Numeric values with context hints
  Fallback, // Default _1, _2 style
}