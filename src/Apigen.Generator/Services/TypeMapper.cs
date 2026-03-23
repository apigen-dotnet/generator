using System.Linq;
using System.Text.Casing;
using Microsoft.OpenApi.Models;
using Apigen.Generator.Models;

namespace Apigen.Generator.Services;

public class TypeMapper
{
  private readonly List<TypeNameOverride> _typeNameOverrides;

  public TypeMapper(List<TypeNameOverride>? typeNameOverrides = null)
  {
    _typeNameOverrides = typeNameOverrides ?? new List<TypeNameOverride>();
  }

  public string MapOpenApiTypeToClr(OpenApiSchema schema, bool useNullable = true)
  {
    if (schema == null)
    {
      return "object";
    }

    string nullable = useNullable && schema.Nullable ? "?" : "";

    if (schema.Type == "string")
    {
      if (schema.Format == "date-time")
      {
        return $"DateTime{nullable}";
      }

      if (schema.Format == "date")
      {
        return $"DateOnly{nullable}";
      }

      if (schema.Format == "uuid" || schema.Format == "guid")
      {
        return $"Guid{nullable}";
      }

      if (schema.Format == "binary")
      {
        return "byte[]" + (useNullable && schema.Nullable ? "?" : "");
      }

      return useNullable ? "string?" : "string";
    }

    if (schema.Type == "integer")
    {
      if (schema.Format == "int64")
      {
        return $"long{nullable}";
      }

      return $"int{nullable}";
    }

    if (schema.Type == "number")
    {
      if (schema.Format == "float")
      {
        return $"float{nullable}";
      }

      if (schema.Format == "double")
      {
        return $"double{nullable}";
      }

      return $"decimal{nullable}";
    }

    if (schema.Type == "boolean")
    {
      return $"bool{nullable}";
    }

    if (schema.Type == "array")
    {
      string itemType = MapOpenApiTypeToClr(schema.Items, useNullable);
      return $"List<{itemType}>?";
    }

    if (schema.Type == "object" || schema.Properties?.Count > 0)
    {
      if (schema.AdditionalProperties != null)
      {
        string valueType = MapOpenApiTypeToClr(schema.AdditionalProperties, useNullable);
        return $"Dictionary<string, {valueType}>?";
      }

      return "object?";
    }

    return "object?";
  }

  public string GetPropertyName(string name)
  {
    if (string.IsNullOrEmpty(name))
    {
      return name;
    }

    // Sanitize invalid C# identifier characters
    // Replace common special characters with their descriptive equivalents
    string cleanName = name
      .Replace("#", "Hash")        // x5t#S256 -> x5tHashS256
      .Replace("@", "At")          // email@domain -> emailAtDomain
      .Replace("$", "Dollar")      // $ref -> DollarRef
      .Replace("%", "Percent")     // rate% -> ratePercent
      .Replace("&", "And")         // Q&A -> QAndA
      .Replace("*", "Star")        // wildcard* -> wildcardStar
      .Replace("+", "Plus")        // C++ -> CPlusPlus
      .Replace("!", "Bang")        // !important -> BangImportant
      .Replace("?", "Question")    // is? -> isQuestion
      .Replace(".", "Dot")         // file.ext -> fileDotExt
      .Replace(",", "Comma")       // a,b -> aCommab
      .Replace(":", "Colon")       // namespace:value -> namespaceColonValue
      .Replace(";", "Semicolon")   // a;b -> aSemicolonb
      .Replace("=", "Equals")      // a=b -> aEqualsb
      .Replace("<", "Lt")          // a<b -> aLtb (less than)
      .Replace(">", "Gt")          // a>b -> aGtb (greater than)
      .Replace("|", "Pipe")        // a|b -> aPipeb
      .Replace("\\", "Backslash")  // path\file -> pathBackslashFile
      .Replace("/", "Slash")       // path/file -> pathSlashFile
      .Replace("~", "Tilde")       // ~temp -> TildeTemp
      .Replace("`", "Backtick")    // `code` -> BacktickCode
      .Replace("^", "Caret")       // a^b -> aCaretb
      .Replace("(", "")            // Remove parentheses
      .Replace(")", "")
      .Replace("{", "")            // Remove braces
      .Replace("}", "")
      .Replace("[", "")            // Remove square brackets
      .Replace("]", "");           // Remove square brackets
      // Note: Underscore, hyphen, and space are handled by ToPascalCase

    // Use the existing ToPascalCase extension which handles:
    // - snake_case (underscores)
    // - kebab-case (hyphens)
    // - spaces
    // - camelCase detection
    return cleanName.ToPascalCase();
  }

  public string GetClassName(string name)
  {
    if (string.IsNullOrEmpty(name))
    {
      return name;
    }

    // Step 0: Apply type name overrides FIRST (before any transformations)
    // This allows custom mappings like "models.Permission" -> "Permission"
    TypeNameOverride? typeOverride = _typeNameOverrides.FirstOrDefault(o => o.Matches(name));
    if (typeOverride != null)
    {
      name = typeOverride.Apply(name);
    }

    // Step 1: Strip HTTP verb prefix (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS)
    name = StripHttpVerbPrefix(name);

    // Step 2: Convert parentheses to spaces to preserve word boundaries
    // "Roles(ByID)" -> "Roles ByID" -> will be properly PascalCased
    string cleanName = name
      .Replace("(", " ")           // Convert to space to preserve word boundaries
      .Replace(")", " ")
      .Replace("{", " ")           // Convert to space to preserve word boundaries
      .Replace("}", " ")
      .Replace("[", " ")           // Convert to space to preserve word boundaries
      .Replace("]", " ")
      .Replace("#", "Hash")
      .Replace("@", "At")
      .Replace("$", "Dollar")
      .Replace("%", "Percent")
      .Replace("&", "And")
      .Replace("*", "Star")
      .Replace("+", "Plus")
      .Replace("!", "Bang")
      .Replace("?", "Question")
      .Replace(".", "Dot")
      .Replace(",", "Comma")
      .Replace(":", "Colon")
      .Replace(";", "Semicolon")
      .Replace("=", "Equals")
      .Replace("<", "Lt")
      .Replace(">", "Gt")
      .Replace("|", "Pipe")
      .Replace("\\", "Backslash")
      .Replace("/", "Slash")
      .Replace("~", "Tilde")
      .Replace("`", "Backtick")
      .Replace("^", "Caret")
      .Replace("-", " ");          // Convert hyphen to space for word boundaries
      // Note: Underscore is valid in C# identifiers, so we keep it

    // Step 3: Use the existing ToPascalCase extension which handles all delimiters
    string result = cleanName.ToPascalCase();

    // Step 4: Normalize common acronyms to proper casing
    result = NormalizeAcronyms(result);

    return result;
  }

  /// <summary>
  /// Strips HTTP verb prefixes from identifiers
  /// e.g., "GETAdminRealms" -> "AdminRealms", "POSTClients" -> "Clients"
  /// </summary>
  private string StripHttpVerbPrefix(string name)
  {
    if (string.IsNullOrEmpty(name))
    {
      return name;
    }

    // Check for common HTTP verbs at the start (case-sensitive for all-caps detection)
    string[] httpVerbs = { "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS" };

    foreach (string verb in httpVerbs)
    {
      // Check if name starts with all-caps HTTP verb followed by uppercase letter
      // e.g., "GETAdmin" (GET + Admin), not "Getter" (Get + ter)
      if (name.StartsWith(verb) && name.Length > verb.Length && char.IsUpper(name[verb.Length]))
      {
        return name.Substring(verb.Length);
      }
    }

    return name;
  }

  /// <summary>
  /// Normalizes common acronyms to proper PascalCase
  /// e.g., "ByID" -> "ById", "UUID" -> "Uuid", "HTTPSRequest" -> "HttpsRequest"
  /// </summary>
  private string NormalizeAcronyms(string name)
  {
    if (string.IsNullOrEmpty(name))
    {
      return name;
    }

    // Dictionary of acronym patterns and their normalized forms
    // Use word boundary regex to avoid partial matches
    Dictionary<string, string> acronyms = new Dictionary<string, string>
    {
      // Two-letter acronyms
      { "ID", "Id" },
      { "UI", "Ui" },
      { "IO", "Io" },
      { "DB", "Db" },
      { "OS", "Os" },

      // Three-letter acronyms
      { "API", "Api" },
      { "URL", "Url" },
      { "URI", "Uri" },
      { "XML", "Xml" },
      { "JSON", "Json" },
      { "HTML", "Html" },
      { "CSS", "Css" },
      { "SQL", "Sql" },
      { "JWT", "Jwt" },
      { "PDF", "Pdf" },
      { "PNG", "Png" },
      { "JPG", "Jpg" },
      { "GIF", "Gif" },
      { "CSV", "Csv" },
      { "TLS", "Tls" },
      { "SSL", "Ssl" },
      { "SSH", "Ssh" },
      { "FTP", "Ftp" },
      { "DNS", "Dns" },
      { "CDN", "Cdn" },
      { "SDK", "Sdk" },

      // Four-letter acronyms
      { "HTTP", "Http" },
      { "HTTPS", "Https" },
      { "UUID", "Uuid" },
      { "GUID", "Guid" },
      { "LDAP", "Ldap" },
      { "SAML", "Saml" },
      { "SMTP", "Smtp" },
      { "REST", "Rest" },
      { "SOAP", "Soap" },

      // Five+ letter acronyms
      { "OAUTH", "OAuth" },  // Special case: OAuth not Oauth
    };

    // Apply replacements for acronyms in PascalCase identifiers
    foreach (var kvp in acronyms)
    {
      // Use regex to match acronym when it's:
      // 1. At the start: "IDToken" -> "IdToken"
      // 2. After lowercase: "ClientID" -> "ClientId", "ByID" -> "ById"
      // 3. Followed by uppercase or end: "GetClientIDAsync" -> "GetClientIdAsync"

      // Pattern: (start OR after lowercase)(ACRONYM)(before uppercase OR end)
      string pattern = $@"(^|(?<=[a-z])){kvp.Key}(?=[A-Z]|$)";
      name = System.Text.RegularExpressions.Regex.Replace(name, pattern, kvp.Value);
    }

    return name;
  }

}