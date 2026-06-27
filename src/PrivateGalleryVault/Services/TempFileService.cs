using System.IO;

namespace PrivateGalleryVault.Services;

public static class TempFileService
{
    private static readonly string BaseTempRoot = Path.Combine(Path.GetTempPath(), "PrivateGalleryVault");
    private static readonly string SessionId = Guid.NewGuid().ToString("N");
    public static string CurrentSessionRoot => Path.Combine(BaseTempRoot, SessionId);

    public static void CleanPreviousSessions()
    {
        try
        {
            if (Directory.Exists(BaseTempRoot))
                Directory.Delete(BaseTempRoot, recursive: true);
        }
        catch
        {
            // 다음 실행 때 다시 정리합니다.
        }
        Directory.CreateDirectory(CurrentSessionRoot);
    }

    public static string CreateTempMediaPath(string mediaId, string extension)
    {
        Directory.CreateDirectory(CurrentSessionRoot);
        extension = extension.StartsWith('.') ? extension : "." + extension;
        return Path.Combine(CurrentSessionRoot, mediaId + extension);
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 미디어 플레이어 핸들이 늦게 풀릴 수 있습니다. 앱 종료 시 전체 정리됩니다.
        }
    }

    public static void CleanCurrentSession()
    {
        try
        {
            if (Directory.Exists(CurrentSessionRoot))
                Directory.Delete(CurrentSessionRoot, recursive: true);
        }
        catch
        {
        }
    }
}
