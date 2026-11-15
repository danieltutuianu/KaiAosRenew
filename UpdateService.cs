using System;
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

        /// <summary>
        /// Check for updates. When <paramref name="silent"/> is true, applies without prompts
        /// (used at startup when Auto update is on). When false (manual button), prompts only if
        /// <paramref name="autoApplyWithoutPrompt"/> is false.
        /// </summary>
        public static async Task CheckForUpdateAsync(
            Window owner,
            bool silent,
            bool autoApplyWithoutPrompt,
            Func<string, string> t)
        {
            if (t == null) t = key => key;
            try
            {
                var mgr = new UpdateManager(new GithubSource(RepoUrl, null, false));
                if (!mgr.IsInstalled)
                {
                    if (!silent)
                        Show(owner, t("UpdateNotInstalled"), t("CheckForUpdates"));
                    return;
                }

                var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(true);
                if (update == null)
                {
                    if (!silent)
                        Show(owner, t("UpdateNone"), t("CheckForUpdates"));
                    return;
                }

                var versionText = update.TargetFullRelease != null
                    ? update.TargetFullRelease.Version.ToString()
                    : "?";
                var applySilent = silent || autoApplyWithoutPrompt;
                if (!applySilent)
                {
                    var msg = string.Format(t("UpdateAvailable"), versionText);
                    var result = owner != null
                        ? MessageBox.Show(owner, msg, t("CheckForUpdates"), MessageBoxButton.YesNo, MessageBoxImage.Question)
                        : MessageBox.Show(msg, t("CheckForUpdates"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result != MessageBoxResult.Yes)
                        return;
                }

                await mgr.DownloadUpdatesAsync(update).ConfigureAwait(true);
                mgr.ApplyUpdatesAndRestart(update);
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    var msg = string.Format(t("UpdateFailed"), ex.Message);
                    Show(owner, msg, t("CheckForUpdates"));
                }
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
