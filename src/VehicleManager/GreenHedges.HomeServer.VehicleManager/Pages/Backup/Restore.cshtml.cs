using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VehicleManager.Services;

namespace VehicleManager.Pages.Backup;

/// <summary>Replaces all the user's data with a backup's (compressed or plain JSON).</summary>
public class RestoreModel(BackupService backups, ILogger<RestoreModel> logger) : PageModel
{
    public const long MaxBytes = 20 * 1024 * 1024;

    private static readonly TimeZoneInfo UkTime = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    [BindProperty] public IFormFile? Upload { get; set; }
    [BindProperty] public bool Confirm { get; set; }

    public BackupCounts Counts { get; private set; } = new(0, 0, 0, 0, 0);

    public async Task OnGetAsync() => Counts = await backups.CountAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        Counts = await backups.CountAsync();

        if (Upload is null || Upload.Length == 0)
            ModelState.AddModelError(nameof(Upload), "Choose a backup file to restore.");
        else if (Upload.Length > MaxBytes)
            ModelState.AddModelError(nameof(Upload), $"Choose a backup file under {MaxBytes / 1024 / 1024} MB.");
        if (!Confirm)
            ModelState.AddModelError(nameof(Confirm), "Tick the box to confirm that your current data will be replaced.");
        if (!ModelState.IsValid) return Page();

        BackupFile backup;
        try
        {
            await using var stream = Upload!.OpenReadStream();
            backup = BackupFile.Read(stream);
        }
        catch (BackupFormatException ex)
        {
            ModelState.AddModelError(nameof(Upload), ex.Message);
            return Page();
        }

        try
        {
            await backups.RestoreAsync(backup);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Failed to restore a backup");
            ModelState.AddModelError(nameof(Upload), "The backup couldn't be saved, so nothing has been changed. " +
                                                     "It may contain values the app doesn't accept.");
            return Page();
        }

        TempData["Message"] = $"Restored the backup from {TimeZoneInfo.ConvertTimeFromUtc(backup.CreatedDate, UkTime):dd/MM/yyyy HH:mm}: {backup.Counts}.";
        return RedirectToPage("/Index");
    }
}
