using Microsoft.OpenApi;
using Apigen.Generator.Services;
using Apigen.Generator.Models;

namespace Apigen.Generator.Tests.Services;

public class OpenApiAnalyzerTests
{
  private readonly OpenApiAnalyzer _analyzer = new();

  private static OpenApiDocument CreateDocument(Dictionary<string, IOpenApiSecurityScheme>? securitySchemes = null)
  {
    return new OpenApiDocument
    {
      Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
      Paths = new OpenApiPaths(),
      Components = new OpenApiComponents
      {
        SecuritySchemes = securitySchemes ?? new Dictionary<string, IOpenApiSecurityScheme>()
      }
    };
  }

  [Fact]
  public void Analyze_ApiKeyInHeader_DetectedCorrectly()
  {
    OpenApiDocument doc = CreateDocument(new Dictionary<string, IOpenApiSecurityScheme>
    {
      ["api_key"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "x-api-key"
      }
    });

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Single(result.AuthenticationSchemes);
    AuthenticationScheme scheme = result.AuthenticationSchemes[0];
    Assert.Equal(AuthSchemeType.ApiKey, scheme.Type);
    Assert.Equal(AuthSchemeLocation.Header, scheme.In);
    Assert.Equal("x-api-key", scheme.HeaderName);
  }

  [Fact]
  public void Analyze_BearerScheme_DetectedCorrectly()
  {
    OpenApiDocument doc = CreateDocument(new Dictionary<string, IOpenApiSecurityScheme>
    {
      ["bearerAuth"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
      }
    });

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Single(result.AuthenticationSchemes);
    AuthenticationScheme scheme = result.AuthenticationSchemes[0];
    Assert.Equal(AuthSchemeType.Http, scheme.Type);
    Assert.Equal(HttpAuthScheme.Bearer, scheme.Scheme);
    Assert.Equal("Authorization", scheme.HeaderName);
  }

  [Fact]
  public void Analyze_BearerScheme_CaseInsensitive()
  {
    OpenApiDocument doc = CreateDocument(new Dictionary<string, IOpenApiSecurityScheme>
    {
      ["auth"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer"
      }
    });

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Single(result.AuthenticationSchemes);
    AuthenticationScheme scheme = result.AuthenticationSchemes[0];
    Assert.Equal(HttpAuthScheme.Bearer, scheme.Scheme);
    Assert.Equal("Authorization", scheme.HeaderName);
  }

  [Fact]
  public void Analyze_CookieScheme_DetectedCorrectly()
  {
    OpenApiDocument doc = CreateDocument(new Dictionary<string, IOpenApiSecurityScheme>
    {
      ["cookieAuth"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Cookie,
        Name = "session_token"
      }
    });

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Single(result.AuthenticationSchemes);
    AuthenticationScheme scheme = result.AuthenticationSchemes[0];
    Assert.Equal(AuthSchemeType.ApiKey, scheme.Type);
    Assert.Equal(AuthSchemeLocation.Cookie, scheme.In);
    Assert.Equal("session_token", scheme.CookieName);
  }

  [Fact]
  public void Analyze_BasicAuth_DetectedCorrectly()
  {
    OpenApiDocument doc = CreateDocument(new Dictionary<string, IOpenApiSecurityScheme>
    {
      ["basicAuth"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.Http,
        Scheme = "basic"
      }
    });

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Single(result.AuthenticationSchemes);
    AuthenticationScheme scheme = result.AuthenticationSchemes[0];
    Assert.Equal(AuthSchemeType.Http, scheme.Type);
    Assert.Equal(HttpAuthScheme.Basic, scheme.Scheme);
    Assert.Equal("Authorization", scheme.HeaderName);
  }

  [Fact]
  public void Analyze_MultipleSchemes_AllDetected()
  {
    OpenApiDocument doc = CreateDocument(new Dictionary<string, IOpenApiSecurityScheme>
    {
      ["bearer"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
      },
      ["cookie"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Cookie,
        Name = "immich_access_token"
      },
      ["api_key"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "x-api-key"
      }
    });

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Equal(3, result.AuthenticationSchemes.Count);

    AuthenticationScheme bearerScheme = result.AuthenticationSchemes.First(s => s.Name == "bearer");
    Assert.Equal(AuthSchemeType.Http, bearerScheme.Type);
    Assert.Equal(HttpAuthScheme.Bearer, bearerScheme.Scheme);

    AuthenticationScheme cookieScheme = result.AuthenticationSchemes.First(s => s.Name == "cookie");
    Assert.Equal(AuthSchemeType.ApiKey, cookieScheme.Type);
    Assert.Equal(AuthSchemeLocation.Cookie, cookieScheme.In);
    Assert.Equal("immich_access_token", cookieScheme.CookieName);

    AuthenticationScheme apiKeyScheme = result.AuthenticationSchemes.First(s => s.Name == "api_key");
    Assert.Equal(AuthSchemeType.ApiKey, apiKeyScheme.Type);
    Assert.Equal(AuthSchemeLocation.Header, apiKeyScheme.In);
    Assert.Equal("x-api-key", apiKeyScheme.HeaderName);
  }

  [Fact]
  public void Analyze_NoSchemes_ReturnsEmptyList()
  {
    OpenApiDocument doc = CreateDocument();

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Empty(result.AuthenticationSchemes);
  }

  [Fact]
  public void Analyze_NoSchemes_PrimaryAuthIsDefault()
  {
    OpenApiDocument doc = CreateDocument();

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.NotNull(result.Authentication);
    Assert.Equal(AuthSchemeType.None, result.Authentication.Type);
  }

  [Fact]
  public void Analyze_OAuth2Scheme_DetectedCorrectly()
  {
    OpenApiDocument doc = CreateDocument(new Dictionary<string, IOpenApiSecurityScheme>
    {
      ["oauth2"] = new OpenApiSecurityScheme
      {
        Type = SecuritySchemeType.OAuth2
      }
    });

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Single(result.AuthenticationSchemes);
    AuthenticationScheme scheme = result.AuthenticationSchemes[0];
    Assert.Equal(AuthSchemeType.OAuth2, scheme.Type);
    Assert.Equal(AuthSchemeLocation.Header, scheme.In);
    Assert.Equal("Authorization", scheme.HeaderName);
    Assert.Equal(HttpAuthScheme.Bearer, scheme.Scheme);
  }

  [Fact]
  public void Analyze_SetsBaseUrl_FromServers()
  {
    OpenApiDocument doc = CreateDocument();
    doc.Servers = [new OpenApiServer { Url = "https://api.example.com/v1" }];

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Equal("https://api.example.com/v1", result.BaseUrl);
  }

  [Fact]
  public void Analyze_NoServers_DefaultsToLocalhost()
  {
    OpenApiDocument doc = CreateDocument();

    OpenApiAnalysis result = _analyzer.Analyze(doc);

    Assert.Equal("https://localhost", result.BaseUrl);
  }
}
