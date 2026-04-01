using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.OpenApi;

namespace Apigen.Generator.Services;

/// <summary>
/// Discovers, compiles, and applies ISpecPatch implementations from .cs files.
/// </summary>
public class SpecPatcher
{
  private readonly List<ISpecPatch> _patches = new();

  /// <summary>
  /// Load and compile all .cs patch files from the given directory.
  /// </summary>
  public void LoadPatches(string patchesDir)
  {
    _patches.Clear();

    if (!Directory.Exists(patchesDir))
    {
      return;
    }

    string[] patchFiles = Directory.GetFiles(patchesDir, "*.cs");
    if (patchFiles.Length == 0)
    {
      return;
    }

    Console.WriteLine($"Compiling {patchFiles.Length} spec patch(es) from {patchesDir}...");

    List<SyntaxTree> syntaxTrees = new();
    foreach (string file in patchFiles)
    {
      string code = File.ReadAllText(file);
      SyntaxTree tree = CSharpSyntaxTree.ParseText(code, path: file);
      syntaxTrees.Add(tree);
    }

    List<MetadataReference> references = GetCompilationReferences();

    CSharpCompilation compilation = CSharpCompilation.Create(
      assemblyName: "SpecPatches_" + Guid.NewGuid().ToString("N")[..8],
      syntaxTrees: syntaxTrees,
      references: references,
      options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    using MemoryStream ms = new();
    var emitResult = compilation.Emit(ms);

    if (!emitResult.Success)
    {
      var errors = emitResult.Diagnostics
        .Where(d => d.Severity == DiagnosticSeverity.Error)
        .Select(d => d.ToString());
      throw new InvalidOperationException(
        $"Failed to compile spec patches:\n{string.Join("\n", errors)}");
    }

    ms.Seek(0, SeekOrigin.Begin);
    Assembly assembly = Assembly.Load(ms.ToArray());

    foreach (Type type in assembly.GetTypes())
    {
      if (typeof(ISpecPatch).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
      {
        if (Activator.CreateInstance(type) is ISpecPatch patch)
        {
          _patches.Add(patch);
        }
      }
    }

    Console.WriteLine($"  Loaded {_patches.Count} patch(es)");
  }

  /// <summary>
  /// Apply all loaded patches to the document, sorted by Order.
  /// Returns the number of patches that made changes.
  /// </summary>
  public int ApplyPatches(OpenApiDocument document)
  {
    if (_patches.Count == 0)
    {
      return 0;
    }

    int applied = 0;
    foreach (ISpecPatch patch in _patches.OrderBy(p => p.Order))
    {
      bool changed = patch.Apply(document);
      Console.WriteLine($"  Patch: {patch.Name}: {(changed ? "applied" : "no-op")}");
      if (changed) applied++;
    }

    return applied;
  }

  /// <summary>
  /// Get the list of loaded patches (for testing).
  /// </summary>
  public IReadOnlyList<ISpecPatch> Patches => _patches.AsReadOnly();

  private static List<MetadataReference> GetCompilationReferences()
  {
    List<MetadataReference> references = new();

    // Add the ISpecPatch / generator assembly
    references.Add(MetadataReference.CreateFromFile(typeof(ISpecPatch).Assembly.Location));

    // Add Microsoft.OpenApi
    references.Add(MetadataReference.CreateFromFile(typeof(OpenApiDocument).Assembly.Location));

    // Add core runtime assemblies
    string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
    references.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));
    references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));
    references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Collections.dll")));
    references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Linq.dll")));
    references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "netstandard.dll")));

    // System.Net.Http is needed because OpenApi 3.x uses HttpMethod as key in Operations dictionary
    references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Net.Http.dll")));

    // System.Text.Json is needed for JsonNode/JsonArray/JsonValue in spec patches
    references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Text.Json.dll")));

    return references;
  }
}
