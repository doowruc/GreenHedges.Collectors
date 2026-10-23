using System.ComponentModel.DataAnnotations;

namespace VehicleManager.ViewModels;

/// <summary>Editing a fuel brand (name) or station (name and brand).</summary>
public class FuelPlaceInput
{
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>A station's brand; not used for brands.</summary>
    [Display(Name = "Brand")]
    public int? BrandId { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];
}
