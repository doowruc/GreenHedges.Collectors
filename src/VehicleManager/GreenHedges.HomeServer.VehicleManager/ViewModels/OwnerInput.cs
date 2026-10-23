using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using GreenHedges.HomeServer.VehicleManager.Models;

namespace GreenHedges.HomeServer.VehicleManager.ViewModels;

public class OwnerInput
{
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<Owner, OwnerInput>> Projection = o => new OwnerInput { Name = o.Name, RowVersion = o.RowVersion };

    public void ApplyTo(Owner owner) => owner.Name = Name;
}
