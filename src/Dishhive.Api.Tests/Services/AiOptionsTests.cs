using Dishhive.Api.Services.Suggestions;
using FluentAssertions;

namespace Dishhive.Api.Tests.Services;

public class AiOptionsTests
{
    [Fact]
    public void IsConfigured_NoProvider_IsFalse()
    {
        new AiOptions { Model = "llama3.1" }.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_NoModel_IsFalse()
    {
        new AiOptions { Provider = "ollama" }.IsConfigured.Should().BeFalse();
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("lmstudio")]
    public void IsConfigured_LocalProvider_NeedsNoApiKey(string provider)
    {
        new AiOptions { Provider = provider, Model = "llama3.1" }.IsConfigured.Should().BeTrue();
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("mistral")]
    public void IsConfigured_CloudProvider_RequiresApiKey(string provider)
    {
        new AiOptions { Provider = provider, Model = "some-model" }.IsConfigured.Should().BeFalse();
        new AiOptions { Provider = provider, Model = "some-model", ApiKey = "key" }.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_OpenAiCompatible_RequiresBaseUrl()
    {
        var withoutUrl = new AiOptions { Provider = "openai-compatible", Model = "m" };
        var withUrl = new AiOptions { Provider = "openai-compatible", Model = "m", BaseUrl = "http://llm.local/v1" };

        withoutUrl.IsConfigured.Should().BeFalse();
        withUrl.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Provider_IsCaseInsensitive()
    {
        new AiOptions { Provider = "Ollama", Model = "llama3.1" }.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void ResolveApiKey_ExplicitKey_WinsOverEnvironment()
    {
        var options = new AiOptions { Provider = "openai", ApiKey = "explicit-key" };

        options.ResolveApiKey().Should().Be("explicit-key");
    }

    [Fact]
    public void ResolveApiKey_FallsBackToStandardEnvVar()
    {
        const string envVar = "OPENAI_API_KEY";
        var original = Environment.GetEnvironmentVariable(envVar);
        try
        {
            Environment.SetEnvironmentVariable(envVar, "env-key");
            new AiOptions { Provider = "openai" }.ResolveApiKey().Should().Be("env-key");
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, original);
        }
    }

    [Theory]
    [InlineData("mistral", "https://api.mistral.ai/v1")]
    [InlineData("ollama", "http://localhost:11434/v1")]
    [InlineData("lmstudio", "http://localhost:1234/v1")]
    public void DefaultEndpoint_PerProvider(string provider, string expected)
    {
        ChatClientFactory.DefaultEndpoint(provider).Should().Be(new Uri(expected));
    }

    [Fact]
    public void DefaultEndpoint_OpenAi_UsesSdkDefault()
    {
        ChatClientFactory.DefaultEndpoint("openai").Should().BeNull();
    }

    [Fact]
    public void Create_UnknownProvider_Throws()
    {
        var options = new AiOptions { Provider = "skynet", Model = "t800", ApiKey = "key" };

        var act = () => ChatClientFactory.Create(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*skynet*");
    }
}
