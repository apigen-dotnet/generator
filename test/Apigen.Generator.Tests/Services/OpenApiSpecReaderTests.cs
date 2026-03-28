using Apigen.Generator.Services;
using Microsoft.OpenApi.Models;

namespace Apigen.Generator.Tests.Services;

public class OpenApiSpecReaderTests
{
  [Fact]
  public void ApplyPathPrefix_PrependsPrefix()
  {
    var document = new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Test", Version = "1.0" },
      Paths = new OpenApiPaths
      {
        ["/users"] = new OpenApiPathItem(),
        ["/users/{id}"] = new OpenApiPathItem(),
      }
    };

    OpenApiSpecReader.ApplyPathPrefix(document, "/api");

    Assert.Contains("/api/users", document.Paths.Keys);
    Assert.Contains("/api/users/{id}", document.Paths.Keys);
    Assert.DoesNotContain("/users", document.Paths.Keys);
  }

  [Fact]
  public void ApplyPathPrefix_EmptyPrefix_NoChange()
  {
    var document = new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Test", Version = "1.0" },
      Paths = new OpenApiPaths
      {
        ["/users"] = new OpenApiPathItem(),
      }
    };

    OpenApiSpecReader.ApplyPathPrefix(document, "");

    Assert.Contains("/users", document.Paths.Keys);
    Assert.Single(document.Paths);
  }

  [Fact]
  public void ApplyPathPrefix_HandlesTrailingSlash()
  {
    var document = new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Test", Version = "1.0" },
      Paths = new OpenApiPaths
      {
        ["/users"] = new OpenApiPathItem(),
      }
    };

    OpenApiSpecReader.ApplyPathPrefix(document, "/api/");

    Assert.Contains("/api/users", document.Paths.Keys);
  }

  [Fact]
  public void ApplyPathPrefix_NullPaths_NoError()
  {
    var document = new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Test", Version = "1.0" },
      Paths = null
    };

    // Should not throw
    OpenApiSpecReader.ApplyPathPrefix(document, "/api");
  }
}
