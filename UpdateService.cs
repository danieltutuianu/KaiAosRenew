using System;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace KaiAosRenew
{
    /// <summary>
    /// Velopack update check / download / apply against GitHub Releases.
    /// </summary>
    internal static class UpdateService
    {
        public const string RepoUrl = "https://github.com/danieltutuianu/KaiAosRenew";
        public const string ServerChannel = "server-win-x86";
        public const string ClientChannel = "client-win-x86";

        public static UpdateManager CreateManager()
        {
            return new UpdateManager(new GithubSource(RepoUrl, null, false));
        }

        /// <summary>
        /// Check only — no download. Returns null if installed build is current or not installed.
        /// </summary>
        public static async Task<UpdateInfo> PeekAsync()
        {
            var mgr = CreateManager();
            if (!mgr.IsInstalled)
                return null;
            return await mgr.CheckForUpdatesAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// Latest Full package version on a Velopack channel feed (does not compare to local install).
        /// Used by the client to detect a newer server release on GitHub.
        /// </summary>
        public static async Task<string> PeekLatestChannelVersionAsync(string channel)
        {
            if (string.IsNullOrWhiteSpace(channel))
                return null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var url = RepoUrl.TrimEnd('/') + "/releases/latest/download/releases." + channel.Trim() + ".json";
                string json;
                using (var wc = new UpdateFeedHelper.TimeoutWebClient(10000))
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "KaiAosRenew-UpdateCheck";
                    json = await wc.DownloadStringTaskAsync(url).ConfigureAwait(true);
                }
                if (string.IsNullOrWhiteSpace(json))
                    return null;
                var feed = VelopackAssetFeed.FromJson(json);
                if (feed == null || feed.Assets == null || feed.Assets.Length == 0)
                    return null;
                string bestFull = null;
                Version bestFullVer = null;
                string bestAny = null;
                Version bestAnyVer = null;
                foreach (var asset in feed.Assets)
                {
                    if (asset == null || asset.Version == null) continue;
                    Version parsed;
                    if (!TryParseVersion(asset.Version.ToString(), out parsed))
                        continue;
                    if (bestAnyVer == null || parsed > bestAnyVer)
                    {
                        bestAnyVer = parsed;
                        bestAny = FormatVersion(parsed);
                    }
                    var typeName = asset.Type.ToString();
                    if (string.Equals(typeName, "Full", StringComparison.OrdinalIgnoreCase)
                        && (bestFullVer == null || parsed > bestFullVer))
                    {
                        bestFullVer = parsed;
                        bestFull = FormatVersion(parsed);
                    }
                }
                return bestFull ?? bestAny;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsNewerVersion(string candidate, string current)
        {
            return UpdateFeedHelper.IsNewerVersion(candidate, current);
        }

        public static bool TryParseVersion(string text, out Version version)
        {
            return UpdateFeedHelper.TryParseVersion(text, out version);
        }

        public static string FormatVersion(Version v)
        {
            return UpdateFeedHelper.FormatVersion(v);
        }

        public static string FormatUpdateVersion(UpdateInfo update)
        {
            if (update == null || update.TargetFullRelease == null)
                return "?";
            return update.TargetFullRelease.Version.ToString();
        }

        /// <summary>
        /// Download and apply a previously peeked update (footer Update button).
        /// <paramref name="progress"/> receives stage (downloading/installing/restarting) and percent 0–100
        /// (-1 when percent is not applicable).
        /// </summary>
        public static async Task ApplyUpdateAsync(
            Window owner,
            UpdateInfo update,
            Func<string, string> t,
            bool prompt,
            Action<string, int> progress = null,
            Action beforeRestart = null)
        {
            if (t == null) t = key => key;
            if (update == null) return;

            var versionText = FormatUpdateVersion(update);
            if (prompt)
            {
                var msg = string.Format(t("UpdateAvailable"), versionText);
                var result = owner != null
                    ? MessageBox.Show(owner, msg, t("CheckForUpdates"), MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBox.Show(msg, t("CheckForUpdates"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            var mgr = CreateManager();
            Action<string, int> report = (stage, pct) =>
            {
                if (progress == null) return;
                if (owner != null && !owner.Dispatcher.CheckAccess())
                {
                    owner.Dispatcher.BeginInvoke(new Action(() => progress(stage, pct)));
                    return;
                }
                progress(stage, pct);
            };
            report("downloading", 0);
            await mgr.DownloadUpdatesAsync(update, percent => report("downloading", percent)).ConfigureAwait(true);
            report("installing", 100);
            report("restarting", -1);
            // Release SingleInstance / listeners only after download succeeded.
            if (beforeRestart != null)
            {
                try { beforeRestart(); }
                catch { }
            }
            mgr.ApplyUpdatesAndRestart(update);
        }

        /// <summary>
        /// Check for updates. When <paramref name="silent"/> is true, applies without prompts
        /// (used at startup when Auto update is on). When false (manual button), prompts only if
        /// <paramref name="autoApplyWithoutPrompt"/> is false.
        /// Returns the UpdateInfo if an update exists (even after declining prompt), else null.
        /// </summary>
        public static async Task<UpdateInfo> CheckForUpdateAsync(
            Window owner,
            bool silent,
            bool autoApplyWithoutPrompt,
            Func<string, string> t)
        {
            if (t == null) t = key => key;
            try
            {
                var mgr = CreateManager();
                if (!mgr.IsInstalled)
                {
                    if (!silent)
                        Show(owner, t("UpdateNotInstalled"), t("CheckForUpdates"));
                    return null;
                }

                var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(true);
                if (update == null)
                {
                    if (!silent)
                        Show(owner, t("UpdateNone"), t("CheckForUpdates"));
                    return null;
                }

                var versionText = FormatUpdateVersion(update);
                var applySilent = silent || autoApplyWithoutPrompt;
                if (!applySilent)
                {
                    var msg = string.Format(t("UpdateAvailable"), versionText);
                    var result = owner != null
                        ? MessageBox.Show(owner, msg, t("CheckForUpdates"), MessageBoxButton.YesNo, MessageBoxImage.Question)
                        : MessageBox.Show(msg, t("CheckForUpdates"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes)
                        return update;
                }

                await mgr.DownloadUpdatesAsync(update).ConfigureAwait(true);
                mgr.ApplyUpdatesAndRestart(update);
                return update;
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    var msg = string.Format(t("UpdateFailed"), ex.Message);
                    Show(owner, msg, t("CheckForUpdates"));
                }
                return null;
            }
        }

        private static void Show(Window owner, string message, string title)
        {
            if (owner != null)
                MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
