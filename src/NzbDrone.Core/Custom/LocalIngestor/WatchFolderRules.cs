using System;
using System.Linq;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.MediaFiles.MediaInfo;

namespace NzbDrone.Core.Custom.LocalIngestor
{
    /// <summary>
    /// Pure decision logic for the local watch folder. Kept free of I/O so it can be unit tested.
    /// </summary>
    public static class WatchFolderRules
    {
        public const ImportMode DefaultImportMode = ImportMode.Copy;
        public const long DefaultMinSizeMb = 50;
        public const int DefaultFailureThreshold = 5;
        public const int DefaultSettleSeconds = 60;

        public static readonly string[] VideoExtensions =
        {
            ".mkv", ".mp4", ".avi", ".ts", ".m4v", ".mov", ".m2ts", ".mpg", ".mpeg", ".wmv", ".webm"
        };

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

        /// <summary>Parses SONARR_WATCH_ALLOW_UPGRADES. Defaults to false.</summary>
        public static bool ParseAllowUpgrades(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True when a previously handled watch file changed size or was rewritten after LastSeen.</summary>
        public static bool HasFileChangedForReeval(long? previousSize, long currentSize, DateTime previousLastSeenUtc, DateTime currentLastWriteUtc)
        {
            if (previousSize.HasValue && currentSize != previousSize.Value)
            {
                return true;
            }

            return currentLastWriteUtc > previousLastSeenUtc;
        }

        /// <summary>Classifies MediaInfo into standard resolution tiers (2160, 1080, 720, 480, or 0 if unknown).</summary>
        public static int GetResolutionTier(MediaInfoModel mediaInfo)
        {
            if (mediaInfo == null || (mediaInfo.Width <= 0 && mediaInfo.Height <= 0))
            {
                return 0;
            }

            if (mediaInfo.Width >= 3200 || mediaInfo.Height >= 2000)
            {
                return 2160;
            }

            if (mediaInfo.Width >= 1600 || mediaInfo.Height >= 900)
            {
                return 1080;
            }

            if (mediaInfo.Width >= 1000 || mediaInfo.Height >= 600)
            {
                return 720;
            }

            return 480;
        }

        /// <summary>Returns the maximum non-commentary audio channel count from MediaInfo (e.g. 6 for 5.1, 2 for stereo).</summary>
        public static int GetAudioChannels(MediaInfoModel mediaInfo)
        {
            var streams = mediaInfo?.AudioStreams;
            if (streams == null || streams.Count == 0)
            {
                return 0;
            }

            var nonCommentary = streams
                .Where(s => s != null && (string.IsNullOrEmpty(s.Title) || !s.Title.Contains("commentary", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var pool = nonCommentary.Count > 0 ? nonCommentary : streams.Where(s => s != null).ToList();
            return pool.Count > 0 ? pool.Max(s => s.Channels) : 0;
        }

        /// <summary>
        /// Evaluates whether candidate MediaInfo is a strict non-regressing upgrade over existing MediaInfo:
        /// higher resolution with equal-or-better audio channels, OR same resolution with more audio channels.
        /// </summary>
        public static bool IsMediaInfoUpgrade(MediaInfoModel candidate, MediaInfoModel existing, out string reason)
        {
            var newRes = GetResolutionTier(candidate);
            var oldRes = GetResolutionTier(existing);
            var newCh = GetAudioChannels(candidate);
            var oldCh = GetAudioChannels(existing);

            if (newRes == 0 && newCh == 0)
            {
                reason = "Candidate media info unavailable";
                return false;
            }

            if (oldRes == 0 && oldCh == 0)
            {
                reason = "Existing library file media info unavailable";
                return false;
            }

            if (newRes > oldRes && (oldCh == 0 || newCh >= oldCh))
            {
                reason = $"MediaInfo upgrade: {oldRes}p/{oldCh}ch -> {newRes}p/{newCh}ch";
                return true;
            }

            if (newRes == oldRes && newCh > oldCh)
            {
                reason = $"MediaInfo audio upgrade: {oldRes}p/{oldCh}ch -> {newRes}p/{newCh}ch";
                return true;
            }

            reason = $"Existing library file is equal or better ({oldRes}p/{oldCh}ch vs {newRes}p/{newCh}ch)";
            return false;
        }

        /// <summary>True when all rejections on a decision are upgrade-comparison rejections rather than hard failures (e.g. Sample, NoAudio).</summary>
        public static bool IsOnlyUpgradeRejection(ImportDecision decision)
        {
            if (decision == null || decision.Approved || decision.Rejections == null || !decision.Rejections.Any())
            {
                return false;
            }

            return decision.Rejections.All(r =>
                r.Reason == ImportRejectionReason.NotQualityUpgrade ||
                r.Reason == ImportRejectionReason.NotRevisionUpgrade ||
                r.Reason == ImportRejectionReason.NotCustomFormatUpgrade ||
                r.Reason == ImportRejectionReason.NotCustomFormatUpgradeAfterRename);
        }
    }
}
