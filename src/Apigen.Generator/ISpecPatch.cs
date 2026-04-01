using Microsoft.OpenApi;

namespace Apigen.Generator;

/// <summary>
/// Implement this interface in a .cs file in the specs/patches/ directory.
/// The generator discovers, compiles, and runs patches automatically.
/// </summary>
public interface ISpecPatch
{
  /// <summary>
  /// Display name for logging.
  /// </summary>
  string Name { get; }

  /// <summary>
  /// Execution order. Lower = earlier. Default: 0.
  /// </summary>
  int Order => 0;

  /// <summary>
  /// Apply the patch to the OpenAPI document in-place.
  /// Return true if changes were made.
  /// </summary>
  bool Apply(OpenApiDocument document);
}
