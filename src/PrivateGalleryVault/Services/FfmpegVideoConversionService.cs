using System.Diagnostics;
using System.IO;

namespace PrivateGalleryVault.Services;

public static class FfmpegVideoConversionService
{
    public sealed record ConversionResult(bool Success, string OutputPath, string Log, int ExitCode);

    public static bool TryFindFfmpeg(out string ffmpegPath)
    {
        var candidates = new List<string>();
        var baseDir = AppContext.BaseDirectory;
        AddCandidate(candidates, Path.Combine(baseDir, "tools", "ffmpeg", "ffmpeg.exe"));
        AddCandidate(candidates, Path.Combine(baseDir, "ffmpeg.exe"));
        AddCandidate(candidates, Path.Combine(Environment.CurrentDirectory, "tools", "ffmpeg", "ffmpeg.exe"));
        AddCandidate(candidates, Path.Combine(Environment.CurrentDirectory, "ffmpeg.exe"));

        var cursor = new DirectoryInfo(baseDir);
        for (var i = 0; i < 8 && cursor != null; i++, cursor = cursor.Parent)
        {
            AddCandidate(candidates, Path.Combine(cursor.FullName, "tools", "ffmpeg", "ffmpeg.exe"));
            AddCandidate(candidates, Path.Combine(cursor.FullName, "src", "PrivateGalleryVault", "tools", "ffmpeg", "ffmpeg.exe"));
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                ffmpegPath = candidate;
                return true;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    ffmpegPath = candidate;
                    return true;
                }
            }
            catch
            {
                // Ignore invalid PATH entries.
            }
        }

        ffmpegPath = string.Empty;
        return false;
    }

    public static async Task<ConversionResult> ConvertToCompatibleMp4Async(
        string inputPath,
        string outputPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("변환할 임시 영상 파일을 찾을 수 없습니다.", inputPath);

        if (!TryFindFfmpeg(out var ffmpegPath))
            throw new FileNotFoundException("ffmpeg.exe를 찾을 수 없습니다. src\\PrivateGalleryVault\\tools\\ffmpeg\\ffmpeg.exe 또는 publish\\win-x64\\tools\\ffmpeg\\ffmpeg.exe 위치에 넣어 주세요.");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? TempFileService.CurrentSessionRoot);
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Path.GetDirectoryName(inputPath) ?? TempFileService.CurrentSessionRoot
        };

        foreach (var arg in new[]
        {
            "-y",
            "-hide_banner",
            "-i", inputPath,
            "-map", "0:v:0",
            "-map", "0:a?",
            "-c:v", "libx264",
            "-pix_fmt", "yuv420p",
            "-profile:v", "high",
            "-level", "4.1",
            "-preset", "veryfast",
            "-crf", "20",
            "-c:a", "aac",
            "-b:a", "160k",
            "-movflags", "+faststart",
            outputPath
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var log = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => AppendLine(log, e.Data, progress);
        process.ErrorDataReceived += (_, e) => AppendLine(log, e.Data, progress);

        AppLogger.Info($"FfmpegConvertStart input={inputPath}; output={outputPath}; ffmpeg={ffmpegPath}");
        if (!process.Start())
            throw new InvalidOperationException("ffmpeg 실행을 시작하지 못했습니다.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var finalLog = log.ToString();
        if (process.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length <= 0)
        {
            AppLogger.Error($"FfmpegConvertFailed exitCode={process.ExitCode}; output={outputPath}; log={finalLog}");
            return new ConversionResult(false, outputPath, finalLog, process.ExitCode);
        }

        AppLogger.Info($"FfmpegConvertEnd output={outputPath}; bytes={new FileInfo(outputPath).Length}");
        return new ConversionResult(true, outputPath, finalLog, process.ExitCode);
    }

    private static void AddCandidate(List<string> candidates, string path)
    {
        if (!candidates.Contains(path, StringComparer.OrdinalIgnoreCase))
            candidates.Add(path);
    }

    private static void AppendLine(System.Text.StringBuilder log, string? line, IProgress<string>? progress)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        log.AppendLine(line);
        if (line.Contains("time=", StringComparison.OrdinalIgnoreCase)
            || line.Contains("frame=", StringComparison.OrdinalIgnoreCase)
            || line.Contains("speed=", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(line.Trim());
        }
    }
}
