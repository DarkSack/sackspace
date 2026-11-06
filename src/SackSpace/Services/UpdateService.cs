using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using SackSpace.Core.Model;

namespace SackSpace.Services;

/// <summary>
/// Asks GitHub once a day whether a newer SackSpace was published (the only request: the
/// latest release of DarkSack/sackspace). Installing downloads the wizard from that release,
/// checks its SHA-256 against the one GitHub lists, and opens it; the wizard closes SackSpace itself.
/// </summary>
public sealed class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly Func<bool> _enabled;
    private string? _told; // the version already announced this session

    public static Version Running { get; } = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0);

    /// <summary>The newer version found, for the tray menu.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>A newer version was found (once per version).</summary>
    public event Action<UpdateInfo>? Found;

    static UpdateService() => Http.DefaultRequestHeaders.UserAgent.ParseAdd($"SackSpace/{Running.ToString(3)}");

    public UpdateService(Func<bool> enabled)
    {
        _enabled = enabled;
        // First look a minute after start (not during it), then once a day.
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = TimeSpan.FromDays(1);
            if (_enabled()) await CheckAsync();
        };
        _timer.Start();
    }

    /// <returns>The newer release, or null when this is the newest (or GitHub could not be asked).</returns>
    public async Task<UpdateInfo?> CheckAsync(bool quiet = true)
    {
        try
        {
            var json = JsonNode.Parse(await Http.GetStringAsync(Updates.LatestUrl));
            Available = json is null ? null : Updates.Newer(json, Running);
            if (Available is { } u && _told != u.Version.ToString())
            {
                _told = u.Version.ToString();
                Found?.Invoke(u);
            }
            return Available;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Warn("No se pudo buscar actualizaciones", ex);
            if (!quiet) throw;
            return null;
        }
    }

    /// <summary>Downloads the wizard into its own temp folder, checks it and opens it.</summary>
    public static async Task InstallAsync(UpdateInfo u)
    {
        if (u.Sha256 is null) throw new InvalidOperationException("GitHub no publicó la huella del instalador; descárgalo desde la página de la versión.");
        var dir = Path.Combine(Path.GetTempPath(), "SackSpace-actualizacion");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Path.GetFileName(u.InstallerName));
        await using (var source = await Http.GetStreamAsync(u.InstallerUrl))
        await using (var target = File.Create(file))
            await source.CopyToAsync(target);
        string hash;
        await using (var check = File.OpenRead(file))
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(check));
        if (hash != u.Sha256)
        {
            File.Delete(file);
            throw new InvalidOperationException("El instalador descargado no coincide con el publicado; no se abrió.");
        }
        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
    }
}
