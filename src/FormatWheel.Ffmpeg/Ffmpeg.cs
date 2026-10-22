using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using FormatWheel.Sdk;

namespace FormatWheel.Ffmpeg;

// Находит ffmpeg.exe. PATH читаем свежий из реестра: приложение запущено при входе в систему
// и не увидит FFmpeg, поставленный через winget уже после этого. Папки winget смотрим и напрямую:
// PATH после установки обновляется не всегда. Пока не нашли — ищем заново при каждом обращении.
internal static class FfmpegExe
{
    private static string? _cached;

    public static string? Path => _cached ??= Find();

    private static string? Find()
    {
        var dirs = new List<string>();
        foreach (var target in new[] { EnvironmentVariableTarget.Process, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Machine })
        {
            string? path = Environment.GetEnvironmentVariable("PATH", target);
            if (path != null) dirs.AddRange(path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        string winget = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet");
        dirs.Add(System.IO.Path.Combine(winget, "Links"));
        try
        {
            string packages = System.IO.Path.Combine(winget, "Packages");
            if (Directory.Exists(packages))
                foreach (string package in Directory.EnumerateDirectories(packages, "Gyan.FFmpeg*"))
                    dirs.AddRange(Directory.EnumerateDirectories(package, "bin", SearchOption.AllDirectories));
        }
        catch { }
        foreach (string dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string candidate = System.IO.Path.Combine(Environment.ExpandEnvironmentVariables(dir), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }
}

// Что выбрано на всех слотах, в одном месте.
// Level: −1 лучше, 0 обычно, 1 сжать, 2 сильно сжать (слот Ctrl).
public sealed record FfmpegSettings(int Level, int? Height, bool Mute, bool Mono, bool Normalize);

// Базовый класс: собирает команду ffmpeg из выбора на слотах, запускает и парсит прогресс.
public abstract class FfmpegConverterBase : IConverter
{
    private static readonly Regex DurationRx = new(@"Duration: (\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex TimeRx = new(@"time=(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    protected abstract string Axis { get; }
    protected abstract HashSet<string> Inputs { get; }
    protected abstract IReadOnlyList<Target> PrimaryTargets { get; }

    // оси дополнительных слотов
    protected string LevelAxis => Axis + ".level";
    protected string HeightAxis => Axis + ".height";
    protected string ExtraAxis => Axis + ".extra";

    // лепесток формата: подпись, подсказка, расширение и аргументы ffmpeg после -i (кодеки, фильтры)
    protected sealed record Target(string Label, string Hint, string Extension, Func<FfmpegSettings, string> Arguments);

    public abstract string Name { get; }
    public bool IsSlow => true; // видео кодируется минутами
    public int MaxParallel => 1; // ffmpeg и так грузит все ядра

    // Файлы берём, даже если FFmpeg не найден: иначе колесо молча не открывается, и непонятно почему.
    // Без FFmpeg на колесе один лепесток «Нет FFmpeg», а конвертация объясняет, что поставить.
    public bool CanHandle(string path) => Inputs.Contains(System.IO.Path.GetExtension(path));

    public IReadOnlyList<WheelOption> MenuFor(WheelSlot slot, IReadOnlyList<string> files) => FfmpegExe.Path == null
        ? (slot == WheelSlot.Primary ? new[] { new WheelOption(Axis, "missing", L.NoFfmpeg, L.NeedsInstall) } : Array.Empty<WheelOption>())
        : slot switch
    {
        WheelSlot.Primary => PrimaryTargets.Select(t => new WheelOption(Axis, t, t.Label, t.Hint)).ToList(),
        // Ctrl — сжатие: одинаковые ступени для всех форматов, каждый переводит их в свои crf/битрейт
        WheelSlot.Ctrl => new[]
        {
            WheelOption.Reset(LevelAxis, L.Normal, L.Balance),
            new WheelOption(LevelAxis, -1, L.Better, L.BiggerFile),
            new WheelOption(LevelAxis, 1, L.Compress, L.SmallerFile),
            new WheelOption(LevelAxis, 2, L.CompressHard, L.MinimalSize),
        },
        _ => ExtraMenu(slot)
    };

    protected abstract IReadOnlyList<WheelOption> ExtraMenu(WheelSlot slot);

    public string Convert(string source, Recipe recipe, Action<double> report, CancellationToken ct)
    {
        string exe = FfmpegExe.Path ?? throw new InvalidOperationException(L.NoFfmpegError);
        var target = recipe.Get<Target>(Axis) ?? PrimaryTargets[0];
        string extra = recipe.Get<string>(ExtraAxis) ?? "";
        var settings = new FfmpegSettings(
            Level: recipe.GetRaw(LevelAxis) is int level ? level : 0,
            Height: recipe.GetRaw(HeightAxis) is int height ? height : null,
            Mute: extra == "mute",
            Mono: extra == "mono",
            Normalize: extra == "normalize");
        string output = OutputPath.New(source, target.Extension);

        string args = $"-hide_banner -nostdin -y -i \"{source}\" {target.Arguments(settings)} \"{output}\"";
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        double duration = 0;
        string lastError = "";
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lastError = line;
            if (duration <= 0 && DurationRx.Match(line) is { Success: true } dm) duration = Secs(dm);
            else if (duration > 0 && TimeRx.Match(line) is { Success: true } tm) report(Math.Clamp(Secs(tm) / duration, 0, 0.99));
        }

        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);
        process.Start();
        // ниже обычного: ffmpeg грузит все ядра, а перетаскивание и окна должны оставаться плавными
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using (ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } }))
            process.WaitForExit();

        ct.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException($"ffmpeg exited with {process.ExitCode}: {lastError}");

        report(1);
        return output;
    }

    private static double Secs(Match m) =>
        double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 3600
        + double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 60
        + double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);

    // ступень сжатия → значение: pick(лучше, обычно, сжать, сильно)
    protected static T Pick<T>(FfmpegSettings s, T better, T normal, T compress, T hard) => s.Level switch
    {
        < 0 => better,
        0 => normal,
        1 => compress,
        _ => hard
    };
}

public sealed class VideoConverter : FfmpegConverterBase
{
    public override string Name => L.Video;
    protected override string Axis => "ffmpeg.video";
    protected override HashSet<string> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".webm", ".wmv", ".flv", ".3gp", ".mts", ".m2ts", ".ts",
        ".mpg", ".mpeg", ".vob", ".ogv", ".f4v", ".asf", ".divx", ".rm", ".rmvb"
    };

    protected override IReadOnlyList<Target> PrimaryTargets { get; } = new[]
    {
        new Target("MP4", L.Universal, "mp4", s => $"{Scale(s)} {H264(s, 23)} {Aac(s)} -movflags +faststart"),
        new Target("HEVC", L.HalfSize, "mp4", s => $"{Scale(s)} -c:v libx265 -preset fast -crf {28 + 4 * s.Level} -tag:v hvc1 -pix_fmt yuv420p {Aac(s)} -movflags +faststart"),
        new Target("WEBM", L.ForWeb, "webm", s => $"{Scale(s)} -c:v libvpx-vp9 -crf {33 + 4 * s.Level} -b:v 0 -row-mt 1 -deadline good -cpu-used 4 {Audio(s, $"-c:a libopus -b:a {Pick(s, 160, 128, 96, 64)}k")}"),
        new Target("MKV", L.Container, "mkv", s => $"{Scale(s)} {H264(s, 23)} {Aac(s)}"),
        new Target("MOV", "Apple", "mov", s => $"{Scale(s)} {H264(s, 20)} {Aac(s)}"),
        new Target("AVI", L.OldPlayers, "avi", s => $"{Scale(s)} -c:v mpeg4 -q:v {Pick(s, 2, 4, 7, 10)} -vtag xvid {Audio(s, "-c:a libmp3lame -q:a 4")}"),
        new Target("WMV", "Windows", "wmv", s => $"{Scale(s)} -c:v wmv2 -q:v {Pick(s, 2, 4, 7, 10)} {Audio(s, $"-c:a wmav2 -b:a {Pick(s, 192, 160, 128, 96)}k")}"),
        new Target("GIF", L.Animation, "gif", Gif),
        new Target("MP3", L.AudioOnly, "mp3", s => $"-vn -c:a libmp3lame -q:a {Pick(s, 0, 2, 4, 6)}"),
        new Target("M4A", L.AudioOnlyAac, "m4a", s => $"-vn -c:a aac -b:a {Pick(s, 256, 192, 128, 96)}k"),
    };

    protected override IReadOnlyList<WheelOption> ExtraMenu(WheelSlot slot) => slot switch
    {
        // Alt — разрешение (только уменьшаем, маленькое видео не растягиваем)
        WheelSlot.Alt => new[]
        {
            WheelOption.Reset(HeightAxis, L.AsIs, L.Resolution),
            new WheelOption(HeightAxis, 1080, "1080p", "Full HD"),
            new WheelOption(HeightAxis, 720, "720p", "HD"),
            new WheelOption(HeightAxis, 480, "480p", L.ForChats),
            new WheelOption(HeightAxis, 360, "360p", L.Tiny),
        },
        WheelSlot.CtrlAlt => new[]
        {
            WheelOption.Reset(ExtraAxis, L.WithSound, L.Sound),
            new WheelOption(ExtraAxis, "mute", L.Mute, L.NoSoundTrack),
        },
        _ => Array.Empty<WheelOption>()
    };

    // H.264/H.265 требуют чётные стороны — у записей экрана они бывают нечётными
    private static string Scale(FfmpegSettings s) => s.Height is int h
        ? $"-vf \"scale=-2:'trunc(min({h},ih)/2)*2'\""
        : "-vf \"scale=trunc(iw/2)*2:trunc(ih/2)*2\"";

    private static string H264(FfmpegSettings s, int crf) =>
        $"-c:v libx264 -preset veryfast -crf {crf + 4 * s.Level} -pix_fmt yuv420p";

    private static string Aac(FfmpegSettings s) => Audio(s, $"-c:a aac -b:a {Pick(s, 192, 160, 128, 96)}k");

    private static string Audio(FfmpegSettings s, string codec) => s.Mute ? "-an" : codec;

    // палитра на весь ролик — без неё GIF выходит рябым
    private static string Gif(FfmpegSettings s)
    {
        string scale = s.Height is int h ? $"scale=-2:'min({h},ih)':flags=lanczos" : $"scale='min({Pick(s, 800, 640, 480, 360)},iw)':-2:flags=lanczos";
        return $"-vf \"fps={Pick(s, 20, 15, 12, 10)},{scale},split[a][b];[a]palettegen[p];[b][p]paletteuse\"";
    }
}

public sealed class AudioConverter : FfmpegConverterBase
{
    public override string Name => L.Audio;
    protected override string Axis => "ffmpeg.audio";
    protected override HashSet<string> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".m4b", ".aac", ".ogg", ".oga", ".opus", ".wma", ".aiff", ".aif",
        ".ac3", ".dts", ".amr", ".mka", ".caf", ".ape", ".wv", ".mp2", ".alac"
    };

    protected override IReadOnlyList<Target> PrimaryTargets { get; } = new[]
    {
        new Target("MP3", L.Universal, "mp3", s => $"{Filters(s)} -c:a libmp3lame -q:a {Pick(s, 0, 2, 4, 6)}"),
        new Target("M4A", "AAC · Apple", "m4a", s => $"{Filters(s)} -c:a aac -b:a {Pick(s, 256, 192, 128, 96)}k"),
        new Target("OPUS", L.TinySize, "opus", s => $"{Filters(s)} -c:a libopus -b:a {Pick(s, 160, 96, 64, 32)}k"),
        new Target("OGG", "Vorbis", "ogg", s => $"{Filters(s)} -c:a libvorbis -q:a {Pick(s, 7, 5, 3, 1)}"),
        new Target("WMA", "Windows", "wma", s => $"{Filters(s)} -c:a wmav2 -b:a {Pick(s, 256, 160, 128, 64)}k"),
        new Target("FLAC", L.Lossless, "flac", s => $"{Filters(s)} -c:a flac -compression_level {Pick(s, 5, 5, 8, 12)}"),
        new Target("ALAC", L.LosslessApple, "m4a", s => $"{Filters(s)} -c:a alac"),
        new Target("WAV", L.Uncompressed, "wav", s => $"{Filters(s)} -c:a pcm_s16le"),
        new Target("AIFF", "Mac", "aiff", s => $"{Filters(s)} -c:a pcm_s16be"),
    };

    protected override IReadOnlyList<WheelOption> ExtraMenu(WheelSlot slot) => slot switch
    {
        WheelSlot.Alt => new[]
        {
            WheelOption.Reset(ExtraAxis, L.AsIs, L.Sound),
            new WheelOption(ExtraAxis, "mono", L.Mono, L.OneChannel),
            new WheelOption(ExtraAxis, "normalize", L.Normalize, L.EvenLoudness),
        },
        _ => Array.Empty<WheelOption>()
    };

    // -vn: обложку альбома из MP3 в WAV/OGG не тащим.
    // loudnorm — громкость как у стримингов (−14 LUFS); он внутри работает на 192 кГц, поэтому возвращаем 48 кГц
    private static string Filters(FfmpegSettings s) =>
        "-vn" + (s.Mono ? " -ac 1" : "") + (s.Normalize ? " -af loudnorm=I=-14:TP=-1:LRA=11,aresample=48000" : "");
}
