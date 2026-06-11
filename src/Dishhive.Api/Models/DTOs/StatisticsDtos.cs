namespace Dishhive.Api.Models.DTOs;

/// <summary>Frequency statistics for one dish (grouped by denormalized dish name)</summary>
public class DishStatisticDto
{
    public string DishName { get; set; } = string.Empty;
    public int TimesPlanned { get; set; }
    public DateOnly LastPlanned { get; set; }

    /// <summary>How often this dish was marked as actually eaten</summary>
    public int TimesEaten { get; set; }

    /// <summary>Average of all member ratings (1–5); null when never rated</summary>
    public double? AverageRating { get; set; }

    /// <summary>Number of ratings of 4 or 5 ("loved")</summary>
    public int LovedCount { get; set; }
}

public class DishStatisticsDto
{
    public List<DishStatisticDto> Dishes { get; set; } = new();

    /// <summary>
    /// Planned meals without a dish name (vague-instruction-only slots); counted
    /// separately so they don't pollute dish frequency (see past-dishes-and-statistics.md)
    /// </summary>
    public int UnspecifiedCount { get; set; }
}

/// <summary>Per-member attendance and dish statistics</summary>
public class MemberStatisticsDto
{
    public Guid MemberId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int MealsAttended { get; set; }

    /// <summary>Attended meals that were marked eaten</summary>
    public int MealsEaten { get; set; }

    /// <summary>Average rating this member gave across all rated meals; null when none</summary>
    public double? AverageRatingGiven { get; set; }

    public List<DishStatisticDto> TopDishes { get; set; } = new();
}
