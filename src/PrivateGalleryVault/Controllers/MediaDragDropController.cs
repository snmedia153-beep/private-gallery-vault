using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace PrivateGalleryVault.Controllers;

public enum MediaDragPayloadKind
{
    None,
    InternalMedia,
    InternalFolder,
    ExternalFiles,
    ExternalFoldersOnly
}

public sealed class MediaDragDropController
{
    public const string MediaInternalDragFormat = "PrivateGalleryVault.MediaIds";
    public const string FolderInternalDragFormat = "PrivateGalleryVault.TopicFolderId";

    public DataObject CreateMediaDragData(IEnumerable<string> mediaIds)
    {
        var data = new DataObject();
        data.SetData(MediaInternalDragFormat, string.Join("\n", mediaIds.Where(id => !string.IsNullOrWhiteSpace(id))));
        return data;
    }

    public DataObject CreateFolderDragData(string folderId)
    {
        var data = new DataObject();
        data.SetData(FolderInternalDragFormat, folderId);
        return data;
    }

    public List<string> GetDraggedMediaIds(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent(MediaInternalDragFormat))
                return [];

            var raw = data.GetData(MediaInternalDragFormat) as string;
            if (string.IsNullOrWhiteSpace(raw))
                return [];

            return raw.Split(['\n', '\r', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public string? GetDraggedFolderId(IDataObject data)
    {
        try
        {
            if (!data.GetDataPresent(FolderInternalDragFormat))
                return null;

            return data.GetData(FolderInternalDragFormat) as string;
        }
        catch
        {
            return null;
        }
    }

    public bool HasDraggedFolder(IDataObject data) => !string.IsNullOrWhiteSpace(GetDraggedFolderId(data));

    public bool TryGetExternalFileDropPaths(IDataObject data, out List<string> files, out List<string> folders)
    {
        files = [];
        folders = [];

        try
        {
            if (!data.GetDataPresent(DataFormats.FileDrop))
                return false;

            if (data.GetData(DataFormats.FileDrop) is not string[] paths)
                return false;

            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                if (File.Exists(rawPath))
                    files.Add(rawPath);
                else if (Directory.Exists(rawPath))
                    folders.Add(rawPath);
            }

            files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            folders = folders.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return files.Count > 0 || folders.Count > 0;
        }
        catch
        {
            files = [];
            folders = [];
            return false;
        }
    }

    public bool IsExternalFileDropData(IDataObject data)
    {
        if (GetDraggedMediaIds(data).Count > 0 || HasDraggedFolder(data))
            return false;

        return TryGetExternalFileDropPaths(data, out var files, out _) && files.Count > 0;
    }

    public MediaDragPayloadKind GetPayloadKind(IDataObject data)
    {
        if (GetDraggedMediaIds(data).Count > 0)
            return MediaDragPayloadKind.InternalMedia;

        if (HasDraggedFolder(data))
            return MediaDragPayloadKind.InternalFolder;

        if (!TryGetExternalFileDropPaths(data, out var files, out var folders))
            return MediaDragPayloadKind.None;

        if (files.Count > 0)
            return MediaDragPayloadKind.ExternalFiles;

        return folders.Count > 0 ? MediaDragPayloadKind.ExternalFoldersOnly : MediaDragPayloadKind.None;
    }

    public static bool HasMovedEnoughForDrag(Point start, Point current)
    {
        return Math.Abs(current.X - start.X) >= SystemParameters.MinimumHorizontalDragDistance
               || Math.Abs(current.Y - start.Y) >= SystemParameters.MinimumVerticalDragDistance;
    }
}
