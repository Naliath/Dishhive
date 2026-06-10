namespace Dishhive.Api.Models.DTOs;

/// <summary>Frequency statistics for one dish (grouped by denormalized dish name)</summary>
public class DishStatisticDto
{
    public string DishName { get; set; } = string.Empty;
    public int TimesPlanned { get; set; }
    public DateOnly LastPlanned { get; set; }
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
    public List<DishStatisticDto> TopDishes { get; set; } = new();
}
