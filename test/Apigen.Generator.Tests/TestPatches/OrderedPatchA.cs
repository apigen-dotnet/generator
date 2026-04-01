using Microsoft.OpenApi;
using Apigen.Generator;

public class OrderedPatchA : ISpecPatch
{
  public string Name => "Ordered A (runs second)";
  public int Order => 2;

  public bool Apply(OpenApiDocument document)
  {
    document.Info.Description += "-A";
    return true;
  }
}
