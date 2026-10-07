using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VehicleManager.Services;

namespace VehicleManager.Pages.Backup;

/// <summary>Downloads a backup of everything the user owns, as a compressed JSON file.</summary>
public class IndexModel(BackupService backups) : PageModel
{
    public BackupCounts Counts { get; private set; } = new(0, 0, 0, 0, 0);

    public async Task OnGetAsync() => Counts = await backups.CountAsync();

    public async Task<IActionResult> OnGetDownloadAsync()
    {
        var backup = await backups.CreateAsync();
        var file = new MemoryStream();
        await backup.WriteAsync(file);
        return File(file.ToArray(), "application/gzip", $"vehicle-manager-backup-{backup.CreatedDate:yyyy-MM-dd-HHmm}.json.gz");
    }
}
