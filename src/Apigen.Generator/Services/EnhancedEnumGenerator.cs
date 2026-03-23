using System.Text.RegularExpressions;
using System.Text;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Any;
using Apigen.Generator.Models;
using Microsoft.OpenApi.Interfaces;

namespace Apigen.Generator.Services;

/// <summary>
/// Advanced enum generation service that creates readable enum names from OpenAPI specifications
/// </summary>
public class EnhancedEnumGenerator
{
  private readonly EnumGenerationOptions _options;

  private readonly HashSet<string> _knownAcronyms = new(StringComparer.OrdinalIgnoreCase)
  {
    "API", "HTTP", "URL", "ID", "UUID", "XML", "JSON", "PDF", "CSV", "ACH", "ATM",
    "USA", "UK", "EU", "USD", "EUR", "GBP", "SMS", "GPS", "IP", "TCP", "UDP",
  };

  public EnhancedEnumGenerator(EnumGenerationOptions options)
  {
    _options = options;
  }

  /// <summary>
  /// Analyzes an OpenAPI enum schema and creates enhanced enum information
  /// </summary>
  public EnhancedEnumInfo AnalyzeEnum(string enumName, OpenApiSchema schema)
  {
    EnhancedEnumInfo enumInfo = new()
    {
      Name = enumName,
      Description = schema.Description ?? "",
      Strategy = DetermineStrategy(schema),
    };

    switch (enumInfo.Strategy)
    {
      case EnumGenerationStrategy.UseDescriptions:
        GenerateFromXEnumDescriptions(enumInfo, schema);
        break;

      case EnumGenerationStrategy.ParseDescription:
        GenerateFromDescriptionParsing(enumInfo, schema);
        break;

      case EnumGenerationStrategy.PreserveValues:
        GenerateFromPreservedValues(enumInfo, schema);
        break;

      case EnumGenerationStrategy.NumericWithContext:
        GenerateFromNumericWithContext(enumInfo, schema);
        break;

      case EnumGenerationStrategy.Fallback:
      default:
        GenerateFromFallback(enumInfo, schema);
        break;
    }

    return enumInfo;
  }

  /// <summary>
  /// Determines the best generation strategy for an enum schema
  /// </summary>
  private EnumGenerationStrategy DetermineStrategy(OpenApiSchema schema)
  {
    List<IOpenApiAny>? enumValues = GetEnumValues(schema);
    if (enumValues == null || !enumValues.Any())
    {
      return EnumGenerationStrategy.Fallback;
    }

    // Check if enum values are developer-friendly
    if (AreDeveloperFriendly(enumValues))
    {
      return EnumGenerationStrategy.PreserveValues;
    }

    // Check for x-enumDescriptions
    if (schema.Extensions.ContainsKey("x-enumDescriptions"))
    {
      return EnumGenerationStrategy.UseDescriptions;
    }

    // Check for patterns in description field
    if (TryParseDescriptionMappings(schema.Description, out _))
    {
      return EnumGenerationStrategy.ParseDescription;
    }

    // Check if numeric with potential context
    if (enumValues.All(v => IsNumericString(v?.ToString() ?? "")))
    {
      return EnumGenerationStrategy.NumericWithContext;
    }

    return EnumGenerationStrategy.Fallback;
  }

  /// <summary>
  /// Generates enum members from x-enumDescriptions extension
  /// </summary>
  private void GenerateFromXEnumDescriptions(EnhancedEnumInfo enumInfo, OpenApiSchema schema)
  {
    enumInfo.HasDescriptions = true;
    List<IOpenApiAny>? enumValues = GetEnumValues(schema);
    Dictionary<string, string> descriptions = GetEnumDescriptions(schema);

    if (enumValues == null)
    {
      return;
    }

    foreach (IOpenApiAny value in enumValues)
    {
      string rawValue = ExtractEnumValueString(value);
      string description = descriptions.TryGetValue(rawValue, out string? desc) ? desc : rawValue;
      string enhancedName = GenerateEnhancedName(description, rawValue);

      enumInfo.Members[enhancedName] = new EnumMemberInfo
      {
        RawValue = rawValue,
        EnhancedName = enhancedName,
        Description = description,
        NumericValue = int.TryParse(rawValue, out int num) ? num : null,
      };
    }
  }

  /// <summary>
  /// Generates enum members by parsing the description field
  /// </summary>
  private void GenerateFromDescriptionParsing(EnhancedEnumInfo enumInfo, OpenApiSchema schema)
  {
    enumInfo.HasDescriptions = true;

    if (TryParseDescriptionMappings(schema.Description, out Dictionary<string, string> mappings))
    {
      foreach (KeyValuePair<string, string> mapping in mappings)
      {
        string enhancedName = GenerateEnhancedName(mapping.Value, mapping.Key);
        enumInfo.Members[enhancedName] = new EnumMemberInfo
        {
          RawValue = mapping.Key,
          EnhancedName = enhancedName,
          Description = mapping.Value,
          NumericValue = int.TryParse(mapping.Key, out int num) ? num : null,
        };
      }
    }
  }

  /// <summary>
  /// Generates enum members preserving the original values (for developer-friendly enums)
  /// </summary>
  private void GenerateFromPreservedValues(EnhancedEnumInfo enumInfo, OpenApiSchema schema)
  {
    List<IOpenApiAny>? enumValues = GetEnumValues(schema);
    if (enumValues == null)
    {
      return;
    }

    foreach (IOpenApiAny value in enumValues)
    {
      string rawValue = ExtractEnumValueString(value);
      string enhancedName = CleanIdentifierName(rawValue);

      enumInfo.Members[enhancedName] = new EnumMemberInfo
      {
        RawValue = rawValue,
        EnhancedName = enhancedName,
        Description = null,
        NumericValue = int.TryParse(rawValue, out int num) ? num : null,
      };
    }
  }

  /// <summary>
  /// Generates enum members for numeric values with contextual hints
  /// </summary>
  private void GenerateFromNumericWithContext(EnhancedEnumInfo enumInfo, OpenApiSchema schema)
  {
    enumInfo.IsNumeric = true;
    List<IOpenApiAny>? enumValues = GetEnumValues(schema);
    if (enumValues == null)
    {
      return;
    }

    // Try to infer context from enum name
    string context = InferContextFromName(enumInfo.Name);

    foreach (IOpenApiAny value in enumValues)
    {
      string rawValue = ExtractEnumValueString(value);
      if (int.TryParse(rawValue, out int numericValue))
      {
        string enhancedName = GenerateNumericEnumName(context, numericValue);
        enumInfo.Members[enhancedName] = new EnumMemberInfo
        {
          RawValue = rawValue,
          EnhancedName = enhancedName,
          Description = null,
          NumericValue = numericValue,
        };
      }
    }
  }

  /// <summary>
  /// Fallback generation using underscore prefix
  /// </summary>
  private void GenerateFromFallback(EnhancedEnumInfo enumInfo, OpenApiSchema schema)
  {
    List<IOpenApiAny>? enumValues = GetEnumValues(schema);
    if (enumValues == null)
    {
      return;
    }

    foreach (IOpenApiAny value in enumValues)
    {
      string rawValue = ExtractEnumValueString(value);
      // Clean the raw value to ensure it's a valid C# identifier
      string enhancedName = $"_{CleanIdentifierName(rawValue)}";

      enumInfo.Members[enhancedName] = new EnumMemberInfo
      {
        RawValue = rawValue,
        EnhancedName = enhancedName,
        Description = null,
        NumericValue = int.TryParse(rawValue, out int num) ? num : null,
      };
    }
  }

  /// <summary>
  /// Generates a clean, PascalCase identifier name from a description
  /// </summary>
  private string GenerateEnhancedName(string description, string fallbackValue)
  {
    if (string.IsNullOrWhiteSpace(description))
    {
      return CleanIdentifierName(fallbackValue);
    }

    // Remove unwanted words
    string cleanDescription = description;
    foreach (string word in _options.NameGeneration.RemoveWords)
    {
      cleanDescription = Regex.Replace(cleanDescription, $@"\b{Regex.Escape(word)}\b", "", RegexOptions.IgnoreCase);
    }

    // Convert to PascalCase identifier
    string pascalCase = ToPascalCase(cleanDescription);

    // Clean up the identifier
    string cleaned = CleanIdentifierName(pascalCase);

    // Truncate if too long
    if (cleaned.Length > _options.NameGeneration.MaxLength)
    {
      cleaned = cleaned.Substring(0, _options.NameGeneration.MaxLength);
    }

    // Ensure it's a valid identifier
    if (string.IsNullOrEmpty(cleaned) || char.IsDigit(cleaned[0]))
    {
      cleaned = $"Value{cleaned}";
    }

    return cleaned;
  }

  /// <summary>
  /// Converts a string to PascalCase
  /// </summary>
  private string ToPascalCase(string input)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return "";
    }

    string[] words = Regex.Split(input, @"[\s\-_\.]+")
      .Where(w => !string.IsNullOrWhiteSpace(w))
      .Select(CapitalizeWord)
      .ToArray();

    return string.Join("", words);
  }

  /// <summary>
  /// Capitalizes a word, preserving known acronyms
  /// </summary>
  private string CapitalizeWord(string word)
  {
    if (string.IsNullOrWhiteSpace(word))
    {
      return "";
    }

    if (_options.NameGeneration.PreserveAcronyms && _knownAcronyms.Contains(word))
    {
      return word.ToUpperInvariant();
    }

    return char.ToUpperInvariant(word[0]) + word.Substring(1).ToLowerInvariant();
  }

  /// <summary>
  /// Cleans a string to be a valid C# identifier
  /// </summary>
  private string CleanIdentifierName(string input)
  {
    if (string.IsNullOrWhiteSpace(input))
    {
      return "";
    }

    StringBuilder result = new();
    foreach (char c in input)
    {
      if (char.IsLetterOrDigit(c) || c == '_')
      {
        result.Append(c);
      }
      else if (!string.IsNullOrEmpty(_options.NameGeneration.InvalidCharReplacement))
      {
        result.Append(_options.NameGeneration.InvalidCharReplacement);
      }
    }

    return result.ToString();
  }

  /// <summary>
  /// Generates an enum name for numeric values with context
  /// </summary>
  private string GenerateNumericEnumName(string context, int value)
  {
    if (!string.IsNullOrEmpty(context))
    {
      return $"{context}{value}";
    }

    return _options.NameGeneration.HandleNumbers == "prefix_underscore"
      ? $"_{value}"
      : SpellOutNumber(value);
  }

  /// <summary>
  /// Attempts to infer context from enum name
  /// </summary>
  private string InferContextFromName(string enumName)
  {
    // Common patterns
    if (enumName.Contains("Status", StringComparison.OrdinalIgnoreCase))
    {
      return "Status";
    }

    if (enumName.Contains("Type", StringComparison.OrdinalIgnoreCase))
    {
      return "Type";
    }

    if (enumName.Contains("Mode", StringComparison.OrdinalIgnoreCase))
    {
      return "Mode";
    }

    if (enumName.Contains("Level", StringComparison.OrdinalIgnoreCase))
    {
      return "Level";
    }

    return "";
  }

  /// <summary>
  /// Spells out small numbers
  /// </summary>
  private string SpellOutNumber(int number)
  {
    return number switch
    {
      0 => "Zero",
      1 => "One",
      2 => "Two",
      3 => "Three",
      4 => "Four",
      5 => "Five",
      6 => "Six",
      7 => "Seven",
      8 => "Eight",
      9 => "Nine",
      10 => "Ten",
      _ => $"_{number}",
    };
  }

  /// <summary>
  /// Gets enum values from OpenAPI schema
  /// </summary>
  private List<IOpenApiAny>? GetEnumValues(OpenApiSchema schema)
  {
    return schema.Enum?.ToList();
  }

  /// <summary>
  /// Gets enum descriptions from x-enumDescriptions extension
  /// </summary>
  private Dictionary<string, string> GetEnumDescriptions(OpenApiSchema schema)
  {
    Dictionary<string, string> result = new();

    if (schema.Extensions.TryGetValue("x-enumDescriptions", out IOpenApiExtension? extension) &&
        extension is OpenApiObject obj)
    {
      foreach (KeyValuePair<string, IOpenApiAny> kvp in obj)
      {
        if (kvp.Value is OpenApiString stringValue)
        {
          result[kvp.Key] = stringValue.Value;
        }
      }
    }

    return result;
  }

  /// <summary>
  /// Checks if enum values are already developer-friendly
  /// </summary>
  private bool AreDeveloperFriendly(List<IOpenApiAny> values)
  {
    return values.All(v =>
    {
      string? str = v?.ToString();
      return !string.IsNullOrEmpty(str) &&
             !IsNumericString(str) &&
             str.Length > 1 &&
             str.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');
    });
  }

  /// <summary>
  /// Checks if a string represents a number
  /// </summary>
  private bool IsNumericString(string value)
  {
    return int.TryParse(value, out _);
  }

  /// <summary>
  /// Attempts to parse description field for enum mappings
  /// </summary>
  private bool TryParseDescriptionMappings(string? description, out Dictionary<string, string> mappings)
  {
    mappings = new Dictionary<string, string>();

    if (string.IsNullOrWhiteSpace(description))
    {
      return false;
    }

    // Pattern: - 1: Description
    string pattern = @"^\s*-\s*(\d+):\s*(.+)$";
    string[] lines = description.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    foreach (string line in lines)
    {
      Match match = Regex.Match(line.Trim(), pattern);
      if (match.Success)
      {
        string value = match.Groups[1].Value;
        string desc = match.Groups[2].Value.Trim();
        mappings[value] = desc;
      }
    }

    return mappings.Count > 0;
  }

  /// <summary>
  /// Extracts the actual string value from an IOpenApiAny
  /// </summary>
  private string ExtractEnumValueString(IOpenApiAny enumValue)
  {
    return enumValue switch
    {
      OpenApiString stringValue => stringValue.Value,
      OpenApiInteger intValue => intValue.Value.ToString(),
      OpenApiDouble doubleValue => doubleValue.Value.ToString(),
      OpenApiBoolean boolValue => boolValue.Value.ToString().ToLowerInvariant(),
      _ => enumValue?.ToString() ?? "",
    };
  }
}