using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using PrivateGalleryVault.Models;

namespace PrivateGalleryVault.ViewModels;

public sealed class MediaCardViewModel : INotifyPropertyChanged
{
    public MediaItem Item { get; }
    public BitmapImage Thumbnail { get; }

    public string Id => Item.Id;
    public string Title => Item.OriginalName;
    public string Badge => Item.Kind switch
    {
        MediaKind.Video => "VIDEO",
        MediaKind.Document => "DOC",
        MediaKind.Archive => "ZIP",
        MediaKind.Other => "FILE",
        _ => "IMAGE"
    };

    public string KindText => Item.Kind switch
    {
        MediaKind.Video => "동영상",
        MediaKind.Document => "문서",
        MediaKind.Archive => "압축파일",
        MediaKind.Other => "기타 파일",
        _ => "이미지"
    };

    public string FavoriteMark => Item.Favorite ? "★" : string.Empty;
    public string SizeText => FormatBytes(Item.SizeBytes);
    public string CreatedText => Item.CreatedUtc.ToLocalTime().ToString("yyyy.MM.dd");
    public string DetailText
    {
        get
        {
            if (Item.Kind == MediaKind.Image && Item.Width > 0 && Item.Height > 0)
                return $"{FormatBytes(Item.SizeBytes)} · {Item.Width}×{Item.Height}";

            var ext = string.IsNullOrWhiteSpace(Item.Extension) ? string.Empty : Item.Extension.TrimStart('.').ToUpperInvariant();
            return string.IsNullOrWhiteSpace(ext)
                ? $"{KindText} · {FormatBytes(Item.SizeBytes)}"
                : $"{KindText} · {ext} · {FormatBytes(Item.SizeBytes)}";
        }
    }

    public MediaCardViewModel(MediaItem item, BitmapImage thumbnail)
    {
        Item = item;
        Thumbnail = thumbnail;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.#} {units[unit]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
