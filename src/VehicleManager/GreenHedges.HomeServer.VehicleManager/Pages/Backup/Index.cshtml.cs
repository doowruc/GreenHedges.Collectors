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
        return File(await backup.ToBytesAsync(), "application/gzip", backup.FileName);
    }
}
