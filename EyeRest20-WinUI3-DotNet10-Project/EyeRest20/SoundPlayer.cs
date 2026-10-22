using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace EyeRest20;

/// <summary>
/// Audio player for built-in and user-supplied (custom) eye rest sounds.
///
/// Reliability design (so an alert is never silently missed, even while a video / music is playing):
///  * Every alert gets its OWN MediaPlayer instance - a new alert can never cancel or
///    replace one that is still loading or playing.
///  * Players use the "Alerts" audio category, which Windows treats as a notification sound
///    (mixed on top of other audio, not paused or ducked like regular media).
///  * System Media Transport Controls integration is disabled, so other media apps
///    (YouTube, Spotify, VLC...) taking over "media focus" can never pause our alert.
///  * A watchdog checks that playback actually started. If the MediaPlayer fails or stays
///    silent, it falls back to the classic Win32 PlaySound API (WAV), and finally to the
///    Windows system chime - so *some* sound is always produced.
/// </summary>
public static class SoundPlayer
{
    // ---- Win32 fallbacks -----------------------------------------------------------
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "PlaySoundW")]
    private static extern bool Win32PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    private const uint MB_ICONASTERISK = 0x00000040; // Standard Windows Asterisk/Chime
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;

    // ---- Sound catalogue -----------------------------------------------------------
    public const string MuteName = "Mute";
    public const string CustomPrefix = "Custom: ";

    /// <summary>File extensions accepted as custom tones (all natively supported by Windows Media Foundation).</summary>
    public static readonly string[] SupportedExtensions = { ".wav", ".mp3", ".m4a", ".wma", ".aac", ".flac" };

    private static readonly (string Name, string File)[] BuiltInSounds =
    {
        ("Tibetan Bowl", "bowl.wav"),
        ("Zen Bell", "bell.wav"),
        ("Soft Chime", "chime.wav"),
        ("Digital Beep", "beep.wav"),
        ("Forest Breeze", "breeze.wav"),
    };

    /// <summary>Display name -> full path for every custom tone found during the last scan.</summary>
    private static readonly Dictionary<string, string> _customSounds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Folder inside the installed app (next to EyeRest20.exe). Files placed in the project's
    /// "CustomSounds" folder before building are copied here automatically.
    /// </summary>
    public static string BundledCustomSoundsFolder =>
        Path.Combine(AppContext.BaseDirectory, "CustomSounds");

    /// <summary>
    /// Writable per-user folder for custom tones. Files can be dropped here at any time
    /// (no rebuild needed) - use the "Open Folder" button in the app to find it.
    /// </summary>
    public static string UserCustomSoundsFolder
    {
        get
        {
            string root;
            try
            {
                // Packaged (MSIX) app: %LOCALAPPDATA%\Packages\<app>\LocalState
                root = ApplicationData.Current.LocalFolder.Path;
            }
            catch
            {
                // Unpackaged fallback
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EyeRest20");
            }
            return Path.Combine(root, "CustomSounds");
        }
    }

    /// <summary>
    /// All selectable sounds: built-ins, then any custom tones, then "Mute".
    /// Call <see cref="RefreshCustomSounds"/> first to pick up newly added files.
    /// </summary>
    public static IReadOnlyList<string> AvailableSounds
    {
        get
        {
            var list = BuiltInSounds.Select(s => s.Name).ToList();
            list.AddRange(_customSounds.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            list.Add(MuteName);
            return list;
        }
    }

    /// <summary>Re-scans both custom sound folders.</summary>
    public static void RefreshCustomSounds()
    {
        _customSounds.Clear();

        foreach (var folder in new[] { UserCustomSoundsFolder, BundledCustomSoundsFolder })
        {
            try
            {
                if (!Directory.Exists(folder)) continue;

                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (!IsSupportedFile(file)) continue;
                    string display = CustomPrefix + Path.GetFileNameWithoutExtension(file);
                    _customSounds.TryAdd(display, file); // user folder wins on duplicate names
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EyeRest] Custom sound scan error ({folder}): {ex.Message}");
            }
        }
    }

    public static bool IsSupportedFile(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Copies a user-chosen audio file into the writable custom sounds folder.</summary>
    /// <returns>The display name of the newly added sound.</returns>
    public static string ImportCustomSound(string sourcePath)
    {
        string folder = UserCustomSoundsFolder;
        Directory.CreateDirectory(folder);
        string dest = Path.Combine(folder, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, dest, overwrite: true);
        RefreshCustomSounds();
        return CustomPrefix + Path.GetFileNameWithoutExtension(dest);
    }

    private static string? ResolvePath(string soundName)
    {
        if (_customSounds.TryGetValue(soundName, out var customPath))
        {
            return customPath;
        }

        string file = BuiltInSounds.FirstOrDefault(s => s.Name == soundName).File ?? "bowl.wav";
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", file);
    }

    // ---- Playback ------------------------------------------------------------------

    // Strong references so in-flight players are never garbage collected mid-sound.
    private static readonly HashSet<MediaPlayer> _activePlayers = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Plays the named sound. Fire-and-forget: safe to call from the UI thread,
    /// never throws, and always produces an audible result unless "Mute" is selected.
    /// </summary>
    public static void PlaySound(string soundName)
    {
        if (soundName == MuteName) return;
        _ = PlaySoundAsync(soundName);
    }

    private static async Task PlaySoundAsync(string soundName)
    {
        string? path = ResolvePath(soundName);

        if (path == null || !File.Exists(path))
        {
            System.Diagnostics.Debug.WriteLine($"[EyeRest] Sound file missing: {path}");
            PlayFallback(null);
            return;
        }

        MediaPlayer? player = null;
        int handled = 0;        // 0 = waiting, 1 = playing/fallback done
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);

            player = new MediaPlayer
            {
                AudioCategory = MediaPlayerAudioCategory.Alerts,
                Volume = 1.0,
                IsLoopingEnabled = false,
                AutoPlay = false
            };
            player.CommandManager.IsEnabled = false; // Don't register with system media controls

            var localPlayer = player;
            localPlayer.PlaybackSession.PlaybackStateChanged += (session, _) =>
            {
                if (session.PlaybackState == MediaPlaybackState.Playing)
                {
                    Interlocked.Exchange(ref handled, 1);
                }
            };
            localPlayer.MediaEnded += (_, _) => Release(localPlayer);
            localPlayer.MediaFailed += (_, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"[EyeRest] MediaPlayer failed: {args.Error} {args.ErrorMessage}");
                if (Interlocked.Exchange(ref handled, 1) == 0)
                {
                    PlayFallback(path);
                }
                Release(localPlayer);
            };

            lock (_lock) { _activePlayers.Add(localPlayer); }

            localPlayer.Source = MediaSource.CreateFromStorageFile(file);
            localPlayer.Play();

            // Watchdog: if playback has not started within 3 seconds, force a fallback sound.
            await Task.Delay(3000);
            if (Interlocked.Exchange(ref handled, 1) == 0)
            {
                System.Diagnostics.Debug.WriteLine("[EyeRest] MediaPlayer watchdog triggered - using fallback.");
                PlayFallback(path);
                Release(localPlayer);
            }

            // Safety net: release the player after 2 minutes even if MediaEnded never fires.
            _ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(_ => Release(localPlayer));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[EyeRest] Sound playback error: {ex.Message}");
            if (Interlocked.Exchange(ref handled, 1) == 0)
            {
                PlayFallback(path);
            }
            if (player != null) Release(player);
        }
    }

    private static void Release(MediaPlayer player)
    {
        bool removed;
        lock (_lock) { removed = _activePlayers.Remove(player); }
        if (!removed) return;

        try { player.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>
    /// Last-resort playback: Win32 PlaySound for WAV files, otherwise the Windows system chime.
    /// </summary>
    private static void PlayFallback(string? path)
    {
        try
        {
            if (path != null &&
                string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase) &&
                Win32PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT))
            {
                return;
            }
        }
        catch { /* continue to next fallback */ }

        try
        {
            if (MessageBeep(MB_ICONASTERISK)) return;
        }
        catch { /* continue to next fallback */ }

        try { Console.Beep(880, 250); } catch { /* nothing else we can do */ }
    }
}
