using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using VehicleManager.Models;

namespace VehicleManager.ViewModels;

public class SettingsInput
{
    [EmailAddress, StringLength(254), Display(Name = "Backup email")]
    public string? BackupEmail { get; set; }

    /// <summary>The row's version when the form was loaded, to detect conflicting edits. Never copied to the entity.</summary>
    public byte[] RowVersion { get; set; } = [];

    public static readonly Expression<Func<UserSettings, SettingsInput>> Projection = s => new SettingsInput
    {
        BackupEmail = s.BackupEmail, RowVersion = s.RowVersion
    };

    public void ApplyTo(UserSettings settings) =>
        settings.BackupEmail = string.IsNullOrWhiteSpace(BackupEmail) ? null : BackupEmail.Trim();
}
