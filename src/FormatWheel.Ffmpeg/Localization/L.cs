using System;
using System.Globalization;

namespace FormatWheel.Ffmpeg;

// Маленькая локализация расширения. Код языка берём так же, как приложение.
internal static class L
{
    private static readonly bool Ru =
        (Environment.GetEnvironmentVariable("FORMATWHEEL_LANG") ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
        .StartsWith("ru", StringComparison.OrdinalIgnoreCase);

    private static string T(string en, string ru) => Ru ? ru : en;

    public static string Video => T("Video (FFmpeg)", "Видео (FFmpeg)");
    public static string Audio => T("Audio (FFmpeg)", "Аудио (FFmpeg)");

    // форматы
    public static string Universal => T("universal", "везде");
    public static string HalfSize => T("H.265, half the size", "H.265, вдвое легче");
    public static string ForWeb => T("for the web", "для сайтов");
    public static string Container => T("any player", "любой плеер");
    public static string OldPlayers => T("old players", "старые плееры");
    public static string Animation => T("animation", "анимация");
    public static string AudioOnly => T("audio only", "только звук");
    public static string AudioOnlyAac => T("audio, AAC", "звук, AAC");
    public static string TinySize => T("tiny size", "мало весит");
    public static string Lossless => T("lossless", "без потерь");
    public static string LosslessApple => T("lossless, Apple", "без потерь, Apple");
    public static string Uncompressed => T("uncompressed", "без сжатия");

    // Ctrl — сжатие
    public static string Normal => T("Normal", "Обычно");
    public static string Balance => T("size/quality balance", "баланс веса и качества");
    public static string Better => T("Better", "Лучше");
    public static string BiggerFile => T("bigger file", "файл крупнее");
    public static string Compress => T("Compress", "Сжать");
    public static string SmallerFile => T("smaller file", "файл меньше");
    public static string CompressHard => T("Max", "Сильно");
    public static string MinimalSize => T("minimal size", "минимум веса");

    // Alt / Ctrl+Alt
    public static string AsIs => T("As is", "Как есть");
    public static string Resolution => T("resolution", "разрешение");
    public static string ForChats => T("for chats", "для чатов");
    public static string Tiny => T("tiny", "крошечное");
    public static string WithSound => T("With sound", "Со звуком");
    public static string Sound => T("sound", "звук");
    public static string Mute => T("Mute", "Без звука");
    public static string NoSoundTrack => T("drop audio track", "убрать дорожку");
    public static string Mono => T("Mono", "Моно");
    public static string OneChannel => T("one channel", "один канал");
    public static string Normalize => T("Normalize", "Выровнять");
    public static string EvenLoudness => T("even loudness", "ровная громкость");

    // FFmpeg не найден
    public static string NoFfmpeg => T("No FFmpeg", "Нет FFmpeg");
    public static string NeedsInstall => T("needs to be installed", "нужно установить");
    public static string NoFfmpegError => T(
        "FFmpeg is not installed. Install it (winget install Gyan.FFmpeg, or from ffmpeg.org) and try again.",
        "FFmpeg не установлен. Установите его (winget install Gyan.FFmpeg или с ffmpeg.org) и попробуйте ещё раз.");
}
