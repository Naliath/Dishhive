using Microsoft.Extensions.AI;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Dishhive.Api.Services.Ai;

/// <summary>
/// Single JSON contract for model-facing schemas, examples, inputs, and parsing.
/// The CLR payload type and these serializer options are the source of truth; prompts
/// must not duplicate wire property names or enum members as string literals.
/// </summary>
internal static class AiJsonContract
{
    internal static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    internal static ChatResponseFormat CreateResponseFormat<T>(
        string schemaName,
        string? schemaDescription = null)
    {
        var schema = AIJsonUtilities.CreateJsonSchema(
            typeof(T),
            serializerOptions: SerializerOptions,
            inferenceOptions: new AIJsonSchemaCreateOptions
            {
                IncludeSchemaKeyword = true,
                TransformSchemaNode = MakeObjectStrict
            });
        return ChatResponseFormat.ForJsonSchema(schema, schemaName, schemaDescription);
    }

    internal static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, SerializerOptions);

    internal static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions);

    internal static string EnumName<TEnum>(TEnum value) where TEnum : struct, Enum =>
        JsonSerializer.SerializeToElement(value, SerializerOptions).GetString()
        ?? throw new InvalidOperationException($"{typeof(TEnum).Name} is not serialized as a string.");

    internal static string EnumValuesJson<TEnum>() where TEnum : struct, Enum =>
        Serialize(Enum.GetValues<TEnum>());

    /// <summary>
    /// Resolves the actual JSON name after naming policies and JsonPropertyName attributes.
    /// Callers pass nameof(T.Property), never a copied wire-name literal.
    /// </summary>
    internal static string PropertyName<T>(string clrPropertyName)
    {
        var property = SerializerOptions.GetTypeInfo(typeof(T)).Properties.SingleOrDefault(candidate =>
            candidate.AttributeProvider is MemberInfo member
            && string.Equals(member.Name, clrPropertyName, StringComparison.Ordinal));
        return property?.Name
            ?? throw new ArgumentException(
                $"{typeof(T).Name} has no serialized property named {clrPropertyName}.",
                nameof(clrPropertyName));
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            // GetTypeInfo is also used before the first serialize call to resolve the
            // model's wire property names. Supplying the resolver makes that lookup
            // deterministic instead of relying on serializer initialization order.
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new JsonStringEnumConverter(
            namingPolicy: null,
            allowIntegerValues: false));
        return options;
    }

    private static JsonNode MakeObjectStrict(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (context.TypeInfo.Kind is not JsonTypeInfoKind.Object
            || schema is not JsonObject schemaObject
            || schemaObject["properties"] is not JsonObject properties)
        {
            return schema;
        }

        schemaObject["additionalProperties"] = false;
        schemaObject["required"] = new JsonArray(properties
            .Select(property => (JsonNode?)JsonValue.Create(property.Key))
            .ToArray());
        return schema;
    }
}
