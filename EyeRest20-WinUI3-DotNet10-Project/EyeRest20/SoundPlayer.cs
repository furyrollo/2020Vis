using System;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace EyeRest20;

/// <summary>
/// Audio player for built-in eye rest sounds with fallback to native Windows chimes.
/// </summary>
public static class SoundPlayer
{
    // Native Win32 API to play standard Windows system chime without WinForms/WPF dependencies
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint uType);

    private const uint MB_ICONASTERISK = 0x00000040; // Standard Windows Asterisk/Chime

    public static readonly string[] AvailableSounds = new[]
    {
        "Tibetan Bowl",
        "Zen Bell",
        "Soft Chime",
        "Digital Beep",
        "Forest Breeze",
        "Mute"
    };

    private static MediaPlayer? _mediaPlayer;

    private static MediaPlayer GetMediaPlayer()
    {
        if (_mediaPlayer == null)
        {
            _mediaPlayer = new MediaPlayer
            {
                Volume = 0.8
            };
        }
        return _mediaPlayer;
    }

    public static void PlaySound(string soundName)
    {
        if (soundName == "Mute") return;

        string fileName = soundName switch
        {
            "Tibetan Bowl" => "bowl.wav",
            "Zen Bell" => "bell.wav",
            "Soft Chime" => "chime.wav",
            "Digital Beep" => "beep.wav",
            "Forest Breeze" => "breeze.wav",
            _ => "bowl.wav"
        };

        try
        {
            // 1. Primary: Try packaged app asset URI (Windows App SDK standard)
            var uri = new Uri($"ms-appx:///Assets/Sounds/{fileName}");
            var player = GetMediaPlayer();
            player.Source = MediaSource.CreateFromUri(uri);
            player.Play();
        }
        catch
        {
            // 2. Reliable Zero-Dependency Fallback: Native Windows System Chime
            try
            {
                MessageBeep(MB_ICONASTERISK);
            }
            catch
            {
                Console.Beep(880, 250);
            }
        }
    }
}