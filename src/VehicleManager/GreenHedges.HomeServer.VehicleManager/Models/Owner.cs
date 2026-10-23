using System.ComponentModel.DataAnnotations;

namespace VehicleManager.Models;

/// <summary>A stored owner. Pages use <c>OwnerInput</c> or their own projections rather than this entity.</summary>
public class Owner : AuditedEntity, IUserOwned
{
    public int Id { get; set; }

    [Required, StringLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    public List<Vehicle> Vehicles { get; set; } = [];
}
