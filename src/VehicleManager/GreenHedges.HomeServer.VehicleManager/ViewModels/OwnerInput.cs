using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using VehicleManager.Models;

namespace VehicleManager.ViewModels;

public class OwnerInput
{
    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    public static readonly Expression<Func<Owner, OwnerInput>> Projection = o => new OwnerInput { Name = o.Name };

    public void ApplyTo(Owner owner) => owner.Name = Name;
}
