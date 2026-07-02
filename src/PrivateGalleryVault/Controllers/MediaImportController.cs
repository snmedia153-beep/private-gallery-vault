using System.IO;
using PrivateGalleryVault.Models;
using PrivateGalleryVault.Services;

namespace PrivateGalleryVault.Controllers;

public enum DuplicateImportChoice
{
    Ask,
    Add,
    AddAll,
    Skip,
    SkipAll
}

public sealed class MediaImportRequest
{
    public IReadOnlyCollection<string> Paths { get; init; } = Array.Empty<string>();
    public Topic Topic { get; init; } = null!;
    public string? FolderId { get; init; }
    public string TargetName { get; init; } = string.Empty;
}

public sealed class MediaImportCallbacks
{
    public Action<string>? SetStatus { get; init; }
    public Action<int>? ConfigureProgress { get; init; }
    public Action<int, int, string>? UpdateProgress { get; init; }
    public Action<string>? ShowScanWarning { get; init; }
    public Action<string, Exception>? ShowFileFailure { get; init; }
    public Func<string, List<MediaItem>, DuplicateImportChoice>? ConfirmDuplicate { get; init; }
    public Func<IReadOnlyList<string>, bool>? ConfirmSecuritySensitiveImport { get; init; }
    public Func<Func<MediaItem>, Task<MediaItem>>? RunStaAsync { get; init; }
}

public sealed class MediaImportResult
{
    public Topic Topic { get; init; } = null!;
    public string? FolderId { get; init; }
    public string TargetName { get; init; } = string.Empty;
    public int Total { get; init; }
    public int Imported { get; init; }
    public int DuplicateAdded { get; init; }
    public int DuplicateSkipped { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public int SecuritySensitiveImported { get; init; }
    public int SecuritySensitiveSkipped { get; init; }

    public bool HasFailures => Failed > 0;

    public string ToStatusMessage()
    {
        var securityText = SecuritySensitiveImported > 0 || SecuritySensitiveSkipped > 0
            ? $", 실행 주의 {SecuritySensitiveImported}개, 주의 건너뜀 {SecuritySensitiveSkipped}개"
            : string.Empty;

        return $"'{TargetName}'에 가져오기 완료: {Imported}개, 중복 추가: {DuplicateAdded}개, 중복 건너뜀: {DuplicateSkipped}개, 기타 건너뜀: {Skipped}개, 실패: {Failed}개{securityText}";
    }
}

/// <summary>
/// 외부/내부 파일 가져오기 흐름을 MainWindow에서 분리한 컨트롤러입니다.
/// UI 표시와 사용자 확인은 콜백으로 위임하고, 암호화 저장/DB 등록/실패 정리만 담당합니다.
/// </summary>
public sealed class MediaImportController
{
    private readonly VaultContext _context;

    public MediaImportController(VaultContext context)
    {
        _context = context;
    }

    public async Task<MediaImportResult> ImportFilesAsync(MediaImportRequest request, MediaImportCallbacks callbacks)
    {
        if (request.Topic == null)
            throw new InvalidOperationException("가져오기 대상 주제가 없습니다.");

        var paths = request.Paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            callbacks.SetStatus?.Invoke("가져올 파일이 없습니다.");
            return CreateEmptyResult(request);
        }

        var scanResult = await Task.Run(() => CollectImportFiles(paths));
        foreach (var error in scanResult.Errors.Take(3))
            callbacks.ShowScanWarning?.Invoke(error);

        var files = scanResult.Files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            callbacks.SetStatus?.Invoke("가져올 파일이 없습니다.");
            return CreateEmptyResult(request);
        }

        var securitySensitiveFiles = files
            .Where(file => MediaVaultService.IsSupported(file) && MediaVaultService.IsSecuritySensitiveFile(file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var allowSecuritySensitiveImport = true;
        var securitySensitiveSkipped = 0;
        if (securitySensitiveFiles.Count > 0)
        {
            allowSecuritySensitiveImport = callbacks.ConfirmSecuritySensitiveImport?.Invoke(securitySensitiveFiles) ?? true;
            if (!allowSecuritySensitiveImport)
            {
                files = files
                    .Where(file => !MediaVaultService.IsSupported(file) || !MediaVaultService.IsSecuritySensitiveFile(file))
                    .ToList();
                securitySensitiveSkipped = securitySensitiveFiles.Count;
            }
        }

        var imported = 0;
        var duplicateAdded = 0;
        var duplicateSkipped = 0;
        var skipped = 0;
        var failed = 0;
        var securitySensitiveImported = 0;
        var processed = 0;
        var total = files.Count;
        var duplicatePolicy = DuplicateImportChoice.Ask;
        var runner = callbacks.RunStaAsync ?? (work => Task.Run(work));

        callbacks.ConfigureProgress?.Invoke(total);

        foreach (var file in files)
        {
            processed++;
            MediaItem? preparedItem = null;

            try
            {
                if (!MediaVaultService.IsSupported(file))
                {
                    skipped++;
                    callbacks.UpdateProgress?.Invoke(processed, total, $"지원하지 않는 파일 건너뜀: {Path.GetFileName(file)}");
                    continue;
                }

                callbacks.UpdateProgress?.Invoke(processed, total, $"중복 검사 중: {Path.GetFileName(file)}");
                var sourceHash = await Task.Run(() => _context.Media.ComputeSourceFingerprint(file));
                var duplicates = _context.Database.GetMediaBySourceHash(sourceHash);

                if (duplicates.Count > 0)
                {
                    var duplicateChoice = duplicatePolicy;
                    if (duplicateChoice == DuplicateImportChoice.Ask)
                    {
                        duplicateChoice = callbacks.ConfirmDuplicate?.Invoke(file, duplicates) ?? DuplicateImportChoice.Skip;
                    }

                    if (duplicateChoice == DuplicateImportChoice.AddAll)
                        duplicatePolicy = DuplicateImportChoice.AddAll;
                    else if (duplicateChoice == DuplicateImportChoice.SkipAll)
                        duplicatePolicy = DuplicateImportChoice.SkipAll;

                    if (duplicateChoice is DuplicateImportChoice.Skip or DuplicateImportChoice.SkipAll)
                    {
                        duplicateSkipped++;
                        callbacks.UpdateProgress?.Invoke(processed, total, $"중복 파일 건너뜀: {Path.GetFileName(file)}");
                        _context.ActivityLogs.Add("duplicate", "중복 파일 건너뜀", Path.GetFileName(file));
                        continue;
                    }

                    duplicateAdded++;
                }

                callbacks.UpdateProgress?.Invoke(processed, total, $"암호화 저장 중: {Path.GetFileName(file)}");
                preparedItem = await runner(() => _context.Media.PrepareMediaImport(file, request.Topic.Id, sourceHash));
                preparedItem.FolderId = request.FolderId;
                _context.Database.AddMedia(preparedItem);
                if (MediaVaultService.IsSecuritySensitiveFile(file))
                    securitySensitiveImported++;
                preparedItem = null;
                imported++;
            }
            catch (Exception ex)
            {
                _context.Media.CleanupPreparedImport(preparedItem);
                failed++;
                callbacks.ShowFileFailure?.Invoke(file, ex);
            }
        }

        var result = new MediaImportResult
        {
            Topic = request.Topic,
            FolderId = request.FolderId,
            TargetName = request.TargetName,
            Total = total,
            Imported = imported,
            DuplicateAdded = duplicateAdded,
            DuplicateSkipped = duplicateSkipped,
            Skipped = skipped,
            Failed = failed,
            SecuritySensitiveImported = securitySensitiveImported,
            SecuritySensitiveSkipped = securitySensitiveSkipped
        };

        _context.ActivityLogs.Add(
            "import",
            "파일 가져오기 완료",
            $"{request.TargetName} · 추가 {imported}개 · 중복 건너뜀 {duplicateSkipped}개 · 실행 주의 {securitySensitiveImported}개 · 주의 건너뜀 {securitySensitiveSkipped}개 · 실패 {failed}개",
            request.Topic.Id,
            request.TargetName,
            failed > 0 ? "partial" : "success");

        return result;
    }

    private static MediaImportResult CreateEmptyResult(MediaImportRequest request)
    {
        return new MediaImportResult
        {
            Topic = request.Topic,
            FolderId = request.FolderId,
            TargetName = request.TargetName,
            Total = 0
        };
    }

    private static (List<string> Files, List<string> Errors) CollectImportFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        var errors = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                    files.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories));
                else if (File.Exists(path))
                    files.Add(path);
            }
            catch (Exception ex)
            {
                errors.Add($"파일 목록을 읽을 수 없습니다: {Path.GetFileName(path)}\n\n{ex.Message}");
            }
        }

        return (files, errors);
    }
}
