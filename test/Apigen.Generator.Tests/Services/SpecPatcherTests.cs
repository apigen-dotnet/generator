using Apigen.Generator.Services;
using Microsoft.OpenApi.Models;

namespace Apigen.Generator.Tests.Services;

public class SpecPatcherTests : IDisposable
{
  private readonly string _testPatchesDir;
  private readonly List<string> _tempDirs = new();

  public SpecPatcherTests()
  {
    // Find the TestPatches directory relative to test assembly output
    string assemblyDir = Path.GetDirectoryName(typeof(SpecPatcherTests).Assembly.Location)!;
    _testPatchesDir = Path.Combine(assemblyDir, "TestPatches");
  }

  public void Dispose()
  {
    foreach (string dir in _tempDirs)
    {
      if (Directory.Exists(dir))
      {
        Directory.Delete(dir, true);
      }
    }
  }

  private string CreateTempDir(params string[] patchFileNames)
  {
    string dir = Path.Combine(Path.GetTempPath(), "SpecPatcherTests_" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(dir);
    _tempDirs.Add(dir);

    foreach (string fileName in patchFileNames)
    {
      string sourceName = fileName.EndsWith(".cs") ? fileName : fileName;
      string source = Path.Combine(_testPatchesDir, sourceName);
      string dest = Path.Combine(dir, fileName);
      File.Copy(source, dest);
    }

    return dir;
  }

  private static OpenApiDocument CreateDocument(string? description = null)
  {
    return new OpenApiDocument
    {
      Info = new OpenApiInfo
      {
        Title = "Test API",
        Version = "1.0",
        Description = description ?? ""
      },
      Paths = new OpenApiPaths()
    };
  }

  [Fact]
  public void LoadPatches_FindsCsFiles()
  {
    string dir = CreateTempDir("AddDescription.cs", "NoOpPatch.cs");
    SpecPatcher patcher = new();

    patcher.LoadPatches(dir);

    Assert.Equal(2, patcher.Patches.Count);
  }

  [Fact]
  public void ApplyPatches_MutatesDocument()
  {
    string dir = CreateTempDir("AddDescription.cs");
    SpecPatcher patcher = new();
    patcher.LoadPatches(dir);
    OpenApiDocument doc = CreateDocument();

    int applied = patcher.ApplyPatches(doc);

    Assert.Equal(1, applied);
    Assert.Equal("patched", doc.Info.Description);
  }

  [Fact]
  public void ApplyPatches_NoOpReturnsZero()
  {
    string dir = CreateTempDir("NoOpPatch.cs");
    SpecPatcher patcher = new();
    patcher.LoadPatches(dir);
    OpenApiDocument doc = CreateDocument();

    int applied = patcher.ApplyPatches(doc);

    Assert.Equal(0, applied);
  }

  [Fact]
  public void ApplyPatches_OrderIsRespected()
  {
    string dir = CreateTempDir("OrderedPatchA.cs", "OrderedPatchB.cs");
    SpecPatcher patcher = new();
    patcher.LoadPatches(dir);
    OpenApiDocument doc = CreateDocument();

    patcher.ApplyPatches(doc);

    // B (Order=1) runs first: sets "B"
    // A (Order=2) runs second: appends "-A"
    Assert.Equal("B-A", doc.Info.Description);
  }

  [Fact]
  public void ApplyPatches_Idempotent()
  {
    string dir = CreateTempDir("AddDescription.cs");
    SpecPatcher patcher = new();
    patcher.LoadPatches(dir);
    OpenApiDocument doc = CreateDocument();

    int first = patcher.ApplyPatches(doc);
    int second = patcher.ApplyPatches(doc);

    Assert.Equal(1, first);
    Assert.Equal(0, second);
    Assert.Equal("patched", doc.Info.Description);
  }

  [Fact]
  public void LoadPatches_CompileError_Throws()
  {
    string dir = CreateTempDir();
    // Copy the .cs.txt file as .cs to trigger compile error
    string source = Path.Combine(_testPatchesDir, "CompileError.cs.txt");
    File.Copy(source, Path.Combine(dir, "CompileError.cs"));

    SpecPatcher patcher = new();

    var ex = Assert.Throws<InvalidOperationException>(() => patcher.LoadPatches(dir));
    Assert.Contains("Failed to compile spec patches", ex.Message);
  }

  [Fact]
  public void LoadPatches_EmptyDirectory_ReturnsEmpty()
  {
    string dir = CreateTempDir(); // no files
    SpecPatcher patcher = new();

    patcher.LoadPatches(dir);

    Assert.Empty(patcher.Patches);
  }

  [Fact]
  public void LoadPatches_NonexistentDirectory_ReturnsEmpty()
  {
    SpecPatcher patcher = new();

    patcher.LoadPatches("/nonexistent/path/that/does/not/exist");

    Assert.Empty(patcher.Patches);
  }
}
