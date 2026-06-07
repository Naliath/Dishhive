using System.ComponentModel.DataAnnotations;
using Dishhive.Api.Models.Agents;

namespace Dishhive.Api.Models.DTOs;

public sealed record AgentStatusDto(
    bool Available,
    string Provider,
    string Model);

public sealed record LearnedRecipeSourceDto(
    string Host,
    string ProviderKey,
    LearnedRecipeSourceStrategy Strategy,
    DateTime LearnedAt,
    DateTime? LastUsedAt,
    int UseCount,
    string SourceUrl);

public sealed class MealSuggestionRequestDto
{
    [Required] public DateOnly Date { get; set; }
    [Required, MaxLength(50)] public string MealType { get; set; } = "Dinner";
    [MaxLength(500)] public string? VagueIntent { get; set; }
    [MaxLength(50)] public string? IntentTag { get; set; }
    public List<Guid> AttendingFamilyMemberIds { get; set; } = new();
}

public sealed record MealSuggestionDto(Guid? RecipeId, string DishLabel, string Reason);

public sealed class ChatRequestDto
{
    [Required, MinLength(1)] public List<ChatTurnDto> Messages { get; set; } = new();
}

public sealed class ChatTurnDto
{
    [Required] public string Role { get; set; } = "user";
    [Required] public string Content { get; set; } = string.Empty;
}

public sealed record ChatReplyDto(string Reply);
