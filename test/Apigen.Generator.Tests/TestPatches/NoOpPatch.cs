using Microsoft.OpenApi.Models;
using Apigen.Generator;

public class NoOpPatch : ISpecPatch
{
  public string Name => "No-op patch";
  public bool Apply(OpenApiDocument document) => false;
}
