namespace Apigen.Generator.Models;

public class CodeFormattingOptions
{
  /// <summary>
  /// Whether to use spaces instead of tabs for indentation
  /// </summary>
  public bool UseSpaces { get; set; } = true;

  /// <summary>
  /// The width of indentation (number of spaces per level if UseSpaces is true, or tab width if false)
  /// </summary>
  public int IndentWidth { get; set; } = 4;

  /// <summary>
  /// Gets the indentation string for a given level
  /// </summary>
  /// <param name="level">The indentation level (0, 1, 2, etc.)</param>
  /// <returns>The appropriate indentation string</returns>
  public string GetIndentation(int level = 1)
  {
    if (UseSpaces)
    {
      return new string(' ', IndentWidth * level);
    }
    else
    {
      return new string('\t', level);
    }
  }
}