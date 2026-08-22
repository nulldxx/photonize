using System.IO;
using FFMpegCore;

namespace Photonize.Services;

/// <summary>
/// Finds ffmpeg once at startup and remembers whether it was there.
///
/// This used to live inline in <c>App.InitializeFFmpegAsync</c>, which configured
/// <see cref="GlobalFFOptions"/> and then threw the path away — so nothing
/// downstream could ask "is ffmpeg actually available?". Slice and Splice need
/// that answer to grey themselves out, the same way the Topaz menu entries hide
/// when Topaz is not installed.
/// </summary>
public static class FFmpegLocator
{
    private const string FFmpegExeName = "ffmpeg.exe";

    /// <summary>The folder holding ffmpeg.exe, or null when it was not found.</summary>
    public static string? BinaryFolder { get; private set; }

    public static bool IsAvailable => BinaryFolder != null;

    /// <summary>Full path to ffmpeg.exe. Only meaningful when <see cref="IsAvailable"/>.</summary>
    public static string ExecutablePath => Path.Combine(BinaryFolder ?? string.Empty, FFmpegExeName);

    /// <summary>
    /// The folders probed, in order. Bundled copy first so a portable install
    /// beats whatever else is on the machine.
    /// </summary>
    public static IReadOnlyList<string> CandidateFolders() => new[]
    {
        // Bundled with the application
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg"),
        // User's local app data
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Photonize", "ffmpeg"),
        // System-wide Program Files installation
        @"C:\Program Files\ffmpeg\bin",
        @"C:\ffmpeg\bin",
        // Chocolatey installation path
        @"C:\ProgramData\chocolatey\lib\ffmpeg\tools\ffmpeg\bin"
    };

    /// <summary>
    /// Probes <paramref name="folders"/> then PATH. Pure apart from the injected
    /// <paramref name="fileExists"/>, so the ordering is unit-testable.
    /// </summary>
    public static string? Locate(IEnumerable<string> folders, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(fileExists);

        foreach (var folder in folders)
        {
            if (!string.IsNullOrWhiteSpace(folder) && fileExists(Path.Combine(folder, FFmpegExeName)))
                return folder;
        }

        // PATH as a last resort. Without this a perfectly working ffmpeg
        // installed anywhere else still triggered the "not found" nag on startup.
        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = folder.Trim().Trim('"');
            if (trimmed.Length == 0)
                continue;

            try
            {
                if (fileExists(Path.Combine(trimmed, FFmpegExeName)))
                    return trimmed;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is not worth failing startup over.
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the probe and points FFMpegCore at whatever was found. Safe to call
    /// more than once.
    /// </summary>
    public static bool Initialize()
    {
        BinaryFolder = Locate(CandidateFolders(), File.Exists);

        if (BinaryFolder != null)
        {
            GlobalFFOptions.Configure(options => options.BinaryFolder = BinaryFolder);
        }

        return IsAvailable;
    }
}
