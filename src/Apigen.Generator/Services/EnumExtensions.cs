using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;

namespace Apigen.Generator.Services;

/// <summary>
/// Extension methods for enhanced enum functionality
/// </summary>
public static class EnumExtensions
{
  /// <summary>
  /// Gets the description of an enum value from the Description attribute
  /// </summary>
  public static string GetDescription<TEnum>(this TEnum enumValue) where TEnum : struct, Enum
  {
    FieldInfo? field = typeof(TEnum).GetField(enumValue.ToString());
    if (field == null)
    {
      return enumValue.ToString();
    }

    DescriptionAttribute? descriptionAttr = field.GetCustomAttribute<DescriptionAttribute>();
    return descriptionAttr?.Description ?? enumValue.ToString();
  }

  /// <summary>
  /// Gets the raw API value for an enum (from EnumMember attribute or numeric conversion)
  /// </summary>
  public static string GetRawValue<TEnum>(this TEnum enumValue) where TEnum : struct, Enum
  {
    FieldInfo? field = typeof(TEnum).GetField(enumValue.ToString());
    if (field == null)
    {
      return enumValue.ToString();
    }

    EnumMemberAttribute? enumMemberAttr = field.GetCustomAttribute<EnumMemberAttribute>();
    if (enumMemberAttr?.Value != null)
    {
      return enumMemberAttr.Value;
    }

    // For _1 style enums, return the numeric part
    string enumName = enumValue.ToString();
    if (enumName.StartsWith("_") && int.TryParse(enumName.Substring(1), out int numericValue))
    {
      return numericValue.ToString();
    }

    return enumName;
  }

  /// <summary>
  /// Checks if an enum value represents an unknown/undefined value
  /// </summary>
  public static bool IsUnknown<TEnum>(this TEnum enumValue) where TEnum : struct, Enum
  {
    string name = enumValue.ToString();
    return name.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
           Convert.ToInt32(enumValue) == -1;
  }

  /// <summary>
  /// Gets all enum values with their descriptions
  /// </summary>
  public static Dictionary<TEnum, string> GetAllDescriptions<TEnum>() where TEnum : struct, Enum
  {
    Dictionary<TEnum, string> result = new();

    foreach (TEnum enumValue in Enum.GetValues<TEnum>())
    {
      result[enumValue] = enumValue.GetDescription();
    }

    return result;
  }

  /// <summary>
  /// Tries to parse a raw API value to an enum
  /// </summary>
  public static bool TryParseRawValue<TEnum>(string rawValue, out TEnum result) where TEnum : struct, Enum
  {
    result = default;

    if (string.IsNullOrEmpty(rawValue))
    {
      return false;
    }

    // Try direct enum parse first
    if (Enum.TryParse<TEnum>(rawValue, true, out result))
    {
      return true;
    }

    // Try numeric conversion
    if (int.TryParse(rawValue, out int numericValue))
    {
      // Try with underscore prefix
      if (Enum.TryParse<TEnum>($"_{numericValue}", true, out result))
      {
        return true;
      }

      // Try direct cast
      if (Enum.IsDefined(typeof(TEnum), numericValue))
      {
        result = (TEnum) (object) numericValue;
        return true;
      }
    }

    // Try finding by EnumMember attribute
    foreach (TEnum enumValue in Enum.GetValues<TEnum>())
    {
      if (enumValue.GetRawValue().Equals(rawValue, StringComparison.OrdinalIgnoreCase))
      {
        result = enumValue;
        return true;
      }
    }

    return false;
  }

  /// <summary>
  /// Gets enum values by category (for grouping in UI)
  /// </summary>
  public static Dictionary<string, List<TEnum>> GroupByCategory<TEnum>() where TEnum : struct, Enum
  {
    Dictionary<string, List<TEnum>> groups = new();

    foreach (TEnum enumValue in Enum.GetValues<TEnum>())
    {
      string category = InferCategory(enumValue.GetDescription());
      if (!groups.ContainsKey(category))
      {
        groups[category] = new List<TEnum>();
      }

      groups[category].Add(enumValue);
    }

    return groups;
  }

  /// <summary>
  /// Infers a category from an enum description (for UI grouping)
  /// </summary>
  private static string InferCategory(string description)
  {
    // Credit card patterns
    if (description.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("Visa", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("MasterCard", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("American Express", StringComparison.OrdinalIgnoreCase))
    {
      return "Credit Cards";
    }

    // Digital wallet patterns
    if (description.Contains("PayPal", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("Google", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("Wallet", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("Venmo", StringComparison.OrdinalIgnoreCase))
    {
      return "Digital Wallets";
    }

    // Bank transfer patterns
    if (description.Contains("Bank", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("Transfer", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("ACH", StringComparison.OrdinalIgnoreCase))
    {
      return "Bank Transfers";
    }

    // Cash and physical
    if (description.Contains("Cash", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("Check", StringComparison.OrdinalIgnoreCase) ||
        description.Contains("Money Order", StringComparison.OrdinalIgnoreCase))
    {
      return "Cash & Checks";
    }

    return "Other";
  }
}

/// <summary>
/// Wrapper struct that provides both enum and raw value access
/// </summary>
public readonly struct SmartEnumValue<TEnum> where TEnum : struct, Enum
{
  private readonly string _rawValue;
  private readonly TEnum? _enumValue;

  public SmartEnumValue(TEnum enumValue)
  {
    _enumValue = enumValue;
    _rawValue = enumValue.GetRawValue();
  }

  public SmartEnumValue(string rawValue)
  {
    _rawValue = rawValue;
    _enumValue = EnumExtensions.TryParseRawValue<TEnum>(rawValue, out TEnum parsed) ? parsed : null;
  }

  /// <summary>
  /// The enum value (or Unknown if not recognized)
  /// </summary>
  public TEnum EnumValue => _enumValue ?? GetUnknownValue();

  /// <summary>
  /// The raw API value
  /// </summary>
  public string RawValue => _rawValue;

  /// <summary>
  /// Whether the value is recognized as a known enum
  /// </summary>
  public bool IsKnown => _enumValue.HasValue;

  /// <summary>
  /// Human-readable description
  /// </summary>
  public string Description => EnumValue.GetDescription();

  // Implicit conversions for ease of use
  public static implicit operator SmartEnumValue<TEnum>(TEnum value)
  {
    return new SmartEnumValue<TEnum>(value);
  }

  public static implicit operator SmartEnumValue<TEnum>(string rawValue)
  {
    return new SmartEnumValue<TEnum>(rawValue);
  }

  public static implicit operator TEnum(SmartEnumValue<TEnum> smartValue)
  {
    return smartValue.EnumValue;
  }

  public override string ToString()
  {
    return Description;
  }

  private static TEnum GetUnknownValue()
  {
    // Look for Unknown value
    if (Enum.TryParse<TEnum>("Unknown", true, out TEnum unknown))
    {
      return unknown;
    }

    // Return first value as fallback
    TEnum[] values = Enum.GetValues<TEnum>();
    return values.Length > 0 ? values[0] : default;
  }
}