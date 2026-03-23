using System;
using System.Linq;
using System.Text;

namespace System.Text.Casing;

public static class StringCaseExtensions
{
  private static readonly char[] Delimiters = [' ', '-', '_', '.'];

  private static string SymbolsPipe(string source, char mainDelimiter, Func<char, bool, char[]> newWordSymbolHandler)
  {
    StringBuilder stringBuilder = new();
    bool isFirstWordCharacter = true;
    bool isNewWord = true;
    foreach (char currentChar in source)
    {
      if (Delimiters.Contains(currentChar))
      {
        if (currentChar == mainDelimiter)
        {
          stringBuilder.Append(currentChar);
          isFirstWordCharacter = true;
        }

        isNewWord = true;
      }
      else if (!char.IsLetterOrDigit(currentChar))
      {
        stringBuilder.Append(currentChar);
        isFirstWordCharacter = true;
        isNewWord = true;
      }
      else if (isNewWord || char.IsUpper(currentChar))
      {
        stringBuilder.Append(newWordSymbolHandler(currentChar, isFirstWordCharacter));
        isFirstWordCharacter = false;
        isNewWord = false;
      }
      else
      {
        stringBuilder.Append(currentChar);
      }
    }

    return stringBuilder.ToString();
  }

  public static string ToDotCase(this string source)
  {
    if (source == null)
    {
      throw new ArgumentNullException(nameof(source));
    }

    return SymbolsPipe(
      source,
      '.',
      (char s, bool disableFrontDelimiter) => disableFrontDelimiter
        ? [char.ToLowerInvariant(s)]
        :
        [
          '.',
          char.ToLowerInvariant(s),
        ]);
  }

  public static string ToCamelCase(this string source)
  {
    if (source == null)
    {
      throw new ArgumentNullException(nameof(source));
    }

    return SymbolsPipe(
      source,
      '\0',
      (char s, bool disableFrontDelimiter) => disableFrontDelimiter
        ? [char.ToLowerInvariant(s)]
        : [char.ToUpperInvariant(s)]);
  }

  public static string ToKebabCase(this string source)
  {
    if (source == null)
    {
      throw new ArgumentNullException(nameof(source));
    }

    return SymbolsPipe(
      source,
      '-',
      (char s, bool disableFrontDelimiter) => disableFrontDelimiter
        ? [char.ToLowerInvariant(s)]
        :
        [
          '-',
          char.ToLowerInvariant(s),
        ]);
  }

  public static string ToSnakeCase(this string source)
  {
    if (source == null)
    {
      throw new ArgumentNullException(nameof(source));
    }

    return SymbolsPipe(
      source,
      '_',
      (char s, bool disableFrontDelimiter) => disableFrontDelimiter
        ? [char.ToLowerInvariant(s)]
        :
        [
          '_',
          char.ToLowerInvariant(s),
        ]);
  }

  public static string ToPascalCase(this string source)
  {
    if (source == null)
    {
      throw new ArgumentNullException("source");
    }

    return SymbolsPipe(source, '\0', (char s, bool _) => new char[1] {char.ToUpperInvariant(s)});
  }

  public static string ToTrainCase(this string source)
  {
    if (source == null)
    {
      throw new ArgumentNullException("source");
    }

    return SymbolsPipe(
      source,
      '-',
      (char s, bool disableFrontDelimiter) => disableFrontDelimiter
        ? [char.ToUpperInvariant(s)]
        :
        [
          '-',
          char.ToUpperInvariant(s),
        ]);
  }

  public static string ToTitleCase(this string source)
  {
    return SymbolsPipe(
      source,
      ' ',
      (char s, bool isFirstWordCharacter) => isFirstWordCharacter
        ? [char.ToUpperInvariant(s)]
        :
        [
          ' ',
          char.ToUpperInvariant(s),
        ]);
  }
}