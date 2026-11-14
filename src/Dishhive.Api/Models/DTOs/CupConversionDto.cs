using System.ComponentModel.DataAnnotations;

namespace Dishhive.Api.Models.DTOs;

/// <summary>An approximate metric equivalent for one US customary cup of a named ingredient.</summary>
public sealed class CupConversionDto
{
    [Required, MaxLength(80)] public string Ingredient { get; set; } = string.Empty;
    [Required] public List<string> Aliases { get; set; } = [];
    [Range(typeof(decimal), "0.01", "10000")] public decimal Quantity { get; set; }
    [Required, RegularExpression("^(g|ml)$")] public string Unit { get; set; } = "g";

    public CupConversionDto() { }
    public CupConversionDto(string ingredient, List<string> aliases, decimal quantity, string unit)
    {
        Ingredient = ingredient; Aliases = aliases; Quantity = quantity; Unit = unit;
    }
}
