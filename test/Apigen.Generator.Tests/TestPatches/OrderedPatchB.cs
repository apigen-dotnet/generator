using Microsoft.OpenApi.Models;
using Apigen.Generator;

public class OrderedPatchB : ISpecPatch
{
  public string Name => "Ordered B (runs first)";
  public int Order => 1;

  public bool Apply(OpenApiDocument document)
  {
    document.Info.Description = "B";
    return true;
  }
}
