using System;
using NzbDrone.Core.MediaFiles.EpisodeImport;

namespace NzbDrone.Core.Custom.LocalIngestor
{
    /// <summary>
    /// Pure decision logic for the local watch folder. Kept free of I/O so it can be unit tested.
    /// </summary>
    public static class WatchFolderRules
    {
        public static readonly string[] VideoExtensions = { ".mkv", ".mp4", ".avi", ".ts" };

        public const ImportMode DefaultImportMode = ImportMode.Copy;
        public const long DefaultMinSizeMb = 50;
        public const int DefaultFailureThreshold = 5;
        public const int DefaultSettleSeconds = 60;

        public static bool IsVideoFile(string path)
        {
            var extension = System.IO.Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return Array.IndexOf(VideoExtensions, extension) >= 0;
        }

        /// <summary>A file is settled once its last write is at least minAge old.</summary>
        public static bool IsSettled(DateTime nowUtc, DateTime lastWriteUtc, TimeSpan minAge)
        {
            return nowUtc - lastWriteUtc >= minAge;
        }

        /// <summary>True when we have seen this file before and its size changed since.</summary>
        public static bool HasGrown(long? previousSize, long currentSize)
        {
            return previousSize.HasValue && currentSize != previousSize.Value;
        }

        public static bool MeetsMinSize(long sizeBytes, long minBytes)
        {
            return sizeBytes >= minBytes;
        }

        /// <summary>Parses SONARR_IMPORT_MODE. Only Move and Copy are supported; unknown or empty values fall back to Copy.</summary>
        public static ImportMode ParseImportMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultImportMode;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "move":
                    return ImportMode.Move;
                case "copy":
                default:
                    return DefaultImportMode;
            }
        }
    }
}
