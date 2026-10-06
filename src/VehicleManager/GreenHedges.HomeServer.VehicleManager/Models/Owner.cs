using System.ComponentModel.DataAnnotations;

namespace VehicleManager.Models;

public class Owner
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    public List<Vehicle> Vehicles { get; set; } = [];
}
