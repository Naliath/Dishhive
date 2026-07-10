using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Tests.Services;

public class AiPromptServiceTests
{
    private static DishhiveDbContext CreateContext() => new(
        new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"prompt-{Guid.NewGuid()}")
            .Options);

    [Fact]
    public async Task GetOverride_WithoutCustomization_ReturnsNull()
    {
        using var context = CreateContext();
        // effective-prompt composition lives on the provider interface
        IAiPromptProvider service = new AiPromptService(context);

        (await service.GetOverrideAsync()).Should().BeNull();
        (await service.GetEffectiveSystemPromptAsync())
            .Should().StartWith(LlmMealSuggestionService.EditableSystemPromptDefault)
            .And.Contain("Reply with ONLY a JSON object");
    }

    [Fact]
    public async Task SetOverride_StoresIt_AndComposesWithProtectedRules()
    {
        using var context = CreateContext();
        var service = new AiPromptService(context);

        await service.SetOverrideAsync("You are a vegan chef.");

        (await service.GetOverrideAsync()).Should().Be("You are a vegan chef.");
        var effective = await ((IAiPromptProvider)service).GetEffectiveSystemPromptAsync();
        effective.Should().StartWith("You are a vegan chef.");
        effective.Should().Contain("NEVER suggest dishes that conflict");
        effective.Should().NotContain(LlmMealSuggestionService.EditableSystemPromptDefault);
    }

    [Fact]
    public async Task SetOverride_TextEqualToDefault_ClearsCustomizationInstead()
    {
        using var context = CreateContext();
        var service = new AiPromptService(context);
        await service.SetOverrideAsync("custom");

        // Pasting the default back should not leave the user flagged as customized
        await service.SetOverrideAsync(LlmMealSuggestionService.EditableSystemPromptDefault);

        (await service.GetOverrideAsync()).Should().BeNull();
        (await service.DefaultChangedSinceCustomizedAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Reset_RemovesOverrideAndBaseline()
    {
        using var context = CreateContext();
        var service = new AiPromptService(context);
        await service.SetOverrideAsync("custom");

        await service.ResetAsync();

        (await service.GetOverrideAsync()).Should().BeNull();
        context.UserSettings.Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultChangedSinceCustomized_DetectsAShippedPromptUpdate()
    {
        using var context = CreateContext();
        var service = new AiPromptService(context);
        await service.SetOverrideAsync("custom");

        (await service.DefaultChangedSinceCustomizedAsync()).Should().BeFalse();

        // Simulate an app update that shipped a different default after customization
        var baseline = await context.UserSettings
            .SingleAsync(s => s.Key == UserSettingKeys.AiSystemPromptBaseline);
        baseline.Value = "an older shipped default";
        await context.SaveChangesAsync();

        (await service.DefaultChangedSinceCustomizedAsync()).Should().BeTrue();
    }
}
