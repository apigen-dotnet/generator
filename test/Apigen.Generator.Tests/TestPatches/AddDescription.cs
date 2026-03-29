using Microsoft.OpenApi.Models;
using Apigen.Generator;

public class AddDescription : ISpecPatch
{
  public string Name => "Add test description";

  public bool Apply(OpenApiDocument document)
  {
    if (document.Info.Description == "patched") return false;
    document.Info.Description = "patched";
    return true;
  }
}
