using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Localization;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace Dishhive.Api.Tests.Services;

public class LlmAiTaskContractTests
{
    private sealed class FixedChatClient(string responseText = "not json") : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => Task.FromResult(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class CapturingMetricsStore : IAiTaskMetricsStore
    {
        public List<AiTaskRun> Runs { get; } = [];
        public Task SaveAsync(AiTaskRun run)
        {
            run.Id = Guid.NewGuid();
            Runs.Add(run);
            return Task.CompletedTask;
        }

        public Task UpdateOutcomeAsync(Guid runId, string outcome, string? error = null)
        {
            var run = Runs.Single(item => item.Id == runId);
            run.Outcome = outcome;
            run.Error = error;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void GeneratedSchemas_AreStrictAndUseCanonicalEnumValues()
    {
        var facts = Schema(LlmRecipeFactsExtractor.JsonSchemaFormat);
        var factsValues = EnumValues(facts, "contains");
        factsValues.Should().Equal(Enum.GetValues<IngredientClass>()
            .Select(AiJsonContract.EnumName));

        var intent = Schema(PlanningIntentContract.JsonSchemaFormat);
        var constraint = intent.GetProperty("properties").GetProperty("constraints")
            .GetProperty("items");
        EnumValues(constraint, "mealType").Should().Equal(
            Enum.GetValues<MealType>().Select(AiJsonContract.EnumName));
        EnumValues(constraint, "course").Should().Equal(
            Enum.GetValues<Course>().Select(AiJsonContract.EnumName));
        EnumValues(constraint, "requiredClasses").Should().Equal(
            Enum.GetValues<IngredientClass>().Select(AiJsonContract.EnumName));
        EnumValues(constraint, "excludedClasses").Should().Equal(
            Enum.GetValues<IngredientClass>().Select(AiJsonContract.EnumName));

        var suggestions = Schema(MealSuggestionResponseContract.JsonSchemaFormat)
            .GetProperty("properties").GetProperty("suggestions").GetProperty("items");
        EnumValues(suggestions, "mealType").Should().Equal(
            Enum.GetValues<MealType>().Select(AiJsonContract.EnumName));
        EnumValues(suggestions, "course").Should().Equal(
            Enum.GetValues<Course>().Select(AiJsonContract.EnumName));

        AssertStrict(facts);
        AssertStrict(intent);
        AssertStrict(Schema(LlmRecipeLocalizationService.JsonSchemaFormat));
        AssertStrict(Schema(LlmRecipeExtractor.JsonSchemaFormat));
        AssertStrict(Schema(MealSuggestionResponseContract.JsonSchemaFormat));
    }

    [Fact]
    public void PromptExamples_AreSerializedFromTheirPayloadContracts()
    {
        using var factsExample = JsonDocument.Parse(AiJsonContract.Serialize(
            new LlmRecipeFactsExtractor.FactsPayload([IngredientClass.Milk])));
        LlmRecipeFactsExtractor.SystemPrompt.Should().Contain(factsExample.RootElement
            .GetProperty(AiJsonContract.PropertyName<LlmRecipeFactsExtractor.FactsPayload>(
                nameof(LlmRecipeFactsExtractor.FactsPayload.Contains)))[0].GetString());

        using var suggestionExample = JsonDocument.Parse(MealSuggestionResponseContract.ExampleJson);
        var item = suggestionExample.RootElement
            .GetProperty(AiJsonContract.PropertyName<WeekSuggestionsPayload>(
                nameof(WeekSuggestionsPayload.Suggestions)))[0];
        item.GetProperty(AiJsonContract.PropertyName<DaySuggestionPayload>(
                nameof(DaySuggestionPayload.MealType)))
            .GetString().Should().Be(AiJsonContract.EnumName(MealType.Dinner));
        item.GetProperty(AiJsonContract.PropertyName<DaySuggestionPayload>(
                nameof(DaySuggestionPayload.Course)))
            .GetString().Should().Be(AiJsonContract.EnumName(Course.Main));
    }

    [Fact]
    public void FactsPayload_UnknownOnlyClassIsNotTreatedAsVerifiedEmpty()
    {
        LlmRecipeFactsExtractor.ParsePayload("""{"contains":["Shellfish"]}""")
            .Should().BeNull();
    }

    [Fact]
    public void SuggestionPayload_UnknownMealTypeIsRejectedByTheTypedContract()
    {
        MealSuggestionResponseContract.Parse(
                """{"suggestions":[{"date":"2026-06-15","dishName":"Soup","mealType":"supper"}]}""")
            .Should().BeNull();
    }

    [Fact]
    public void FactsPayload_KnownClassesRemainCanonicalAndDistinct()
    {
        LlmRecipeFactsExtractor.ParsePayload("""{"contains":["Milk","Gluten","Milk"]}""")
            .Should().Equal(IngredientClass.Gluten, IngredientClass.Milk);
    }

    [Fact]
    public void TranslationValidation_RejectsBlankArrayEntries()
    {
        var recipe = new Recipe
        {
            Title = "Source",
            Ingredients = { new RecipeIngredient { Name = "milk", OriginalText = "milk" } },
            Steps = { new RecipeStep { Instruction = "mix" } }
        };
        var translation = new LocalizedRecipeText("Titel", null, [""], ["Meng"]);

        LlmRecipeLocalizationService.Validate(translation, recipe)
            .Should().Be("A translated ingredient name was empty");
    }

    [Fact]
    public void TranslationChunking_PreservesEveryIndexedItemWithoutRepeatingHeaders()
    {
        var recipe = new Recipe
        {
            Title = "Source",
            Description = new string('d', 1500),
            Ingredients =
            {
                new RecipeIngredient { SortOrder = 0, Name = "milk", OriginalText = "milk" },
                new RecipeIngredient { SortOrder = 1, Name = "salt", OriginalText = "salt" }
            },
            Steps =
            {
                new RecipeStep { StepNumber = 1, Instruction = new string('a', 2000) },
                new RecipeStep { StepNumber = 2, Instruction = new string('b', 2000) },
                new RecipeStep { StepNumber = 3, Instruction = new string('c', 2000) }
            }
        };

        var chunks = LlmRecipeLocalizationService.BuildChunks(recipe, 1024);

        chunks.Should().HaveCountGreaterThan(1);
        chunks.Count(chunk => chunk.IncludesHeader).Should().Be(1);
        chunks.SelectMany(chunk => chunk.IngredientNames).Select(item => item.Index)
            .Should().Equal(0, 1);
        chunks.SelectMany(chunk => chunk.Steps).Select(item => item.Index)
            .Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task Translation_InvalidJsonIsRecordedAsParseFailure()
    {
        var options = new AiOptions { Provider = "lmstudio", Model = "test" };
        var metrics = new CapturingMetricsStore();
        var gate = new AiExecutionGate(
            new FixedChatClient(), options, metrics, NullLogger<AiExecutionGate>.Instance);
        var service = new LlmRecipeLocalizationService(
            gate, options, new SupportedLanguageCatalog(),
            NullLogger<LlmRecipeLocalizationService>.Instance);
        var recipe = new Recipe
        {
            Id = Guid.NewGuid(),
            Title = "Source",
            Ingredients = { new RecipeIngredient { Name = "milk", OriginalText = "milk" } },
            Steps = { new RecipeStep { Instruction = "mix" } }
        };

        var result = await service.TranslateAsync(recipe, "nl");

        result.Should().BeNull();
        metrics.Runs.Should().HaveCount(2);
        metrics.Runs.Should().OnlyContain(run => run.Outcome == "parseFailure");
        metrics.Runs.Select(run => run.OperationId).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Import_InvalidJsonIsRecordedAsParseFailure()
    {
        var options = new AiOptions { Provider = "lmstudio", Model = "test" };
        var metrics = new CapturingMetricsStore();
        var gate = new AiExecutionGate(
            new FixedChatClient(), options, metrics, NullLogger<AiExecutionGate>.Instance);
        var service = new LlmRecipeExtractor(
            gate, options, NullLogger<LlmRecipeExtractor>.Instance);

        var result = await service.ExtractAsync(
            "<html><body>This is a sufficiently long recipe page with ingredients and preparation details.</body></html>",
            new Uri("https://recipes.example/item"));

        result.Should().BeNull();
        metrics.Runs.Should().HaveCount(2);
        metrics.Runs.Should().OnlyContain(run => run.Outcome == "parseFailure");
    }

    [Fact]
    public void ImportValidation_RejectsRecipeWithoutIngredientsOrInstructions()
    {
        var payload = new LlmRecipeExtractor.ExtractionPayload(
            "Title", null, [], [], 4, null, null, null, null, null, null);

        LlmRecipeExtractor.Validate(payload).Should().Be("The recipe had no usable ingredients");
    }

    [Fact]
    public void ImportPromptBudget_PreservesStartAndEndWithVisibleMarker()
    {
        var input = new string('a', 4000) + new string('z', 4000);

        var trimmed = LlmRecipeExtractor.TrimToPromptBudget(input, 1000);

        trimmed.Should().StartWith("aaa").And.EndWith("zzz").And.Contain("[page text trimmed]");
        trimmed.Length.Should().BeLessThan(input.Length);
    }

    private static JsonElement Schema(ChatResponseFormat format) =>
        ((ChatResponseFormatJson)format).Schema!.Value;

    private static IEnumerable<string?> EnumValues(JsonElement objectSchema, string propertyName)
    {
        var schema = objectSchema.GetProperty("properties").GetProperty(propertyName);
        if (schema.TryGetProperty("items", out var items)) schema = items;
        return schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString());
    }

    private static void AssertStrict(JsonElement schema)
    {
        if (schema.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("properties", out var properties))
            {
                schema.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
                schema.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                    .Should().BeEquivalentTo(properties.EnumerateObject().Select(property => property.Name));
            }
            foreach (var property in schema.EnumerateObject()) AssertStrict(property.Value);
        }
        else if (schema.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in schema.EnumerateArray()) AssertStrict(item);
        }
    }
}
