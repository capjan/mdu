using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Markdown.Renderer;

internal static partial class MediaLinkEmbedder
{
    internal static bool IsProviderLink(string? value) => value is not null &&
        Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && GetEmbed(uri).VideoId is not null;

    private static readonly Regex StandaloneLinkParagraphRegex = StandaloneLinkParagraphRegexPattern();

    [GeneratedRegex(
        "<p>\\s*<a href=\"(?<url>[^\"]+)\"[^>]*>(?<label>.*?)</a>\\s*</p>",
        RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex StandaloneLinkParagraphRegexPattern();

    private static readonly Regex YouTubeIdRegex = YouTubeIdRegexPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.Compiled)]
    private static partial Regex YouTubeIdRegexPattern();
    private static readonly Regex VimeoIdRegex = VimeoIdRegexPattern();

    [GeneratedRegex("^[0-9]+$", RegexOptions.Compiled)]
    private static partial Regex VimeoIdRegexPattern();

    public const string Script = """
        (() => {
            const settingsDialog = document.getElementById("media-settings-dialog");
            const settingsStatus = document.getElementById("media-settings-status");
            const providerInputs = new Map(Array.from(
                document.querySelectorAll("#media-settings-dialog [data-media-provider]"),
                (input) => [input.dataset.mediaProvider, input]));
            const providers = [...providerInputs.keys()];
            const rootPageLink = document.querySelector(".site-nav-link");
            let sitePath = location.pathname;
            try {
                if (rootPageLink) sitePath = new URL(rootPageLink.href, location.href).pathname;
            } catch { }
            const storageKey = `mdu-media-consent:${sitePath}`;

            const readPermissions = () => {
                try {
                    const saved = JSON.parse(localStorage.getItem(storageKey) || "[]");
                    return Array.isArray(saved) ? saved.filter((provider) => typeof provider === "string") : [];
                } catch {
                    return [];
                }
            };

            const savePermissions = (permissions) => {
                try {
                    if (permissions.length === 0) localStorage.removeItem(storageKey);
                    else localStorage.setItem(storageKey, JSON.stringify(permissions));
                    return true;
                } catch {
                    return false;
                }
            };

            const loadVideo = (container) => {
                if (!container || container.querySelector("iframe")) return;

                const iframe = document.createElement("iframe");
                iframe.src = container.dataset.embedUrl;
                iframe.title = `${container.dataset.provider} video player`;
                iframe.allow = "fullscreen; picture-in-picture";
                iframe.referrerPolicy = "strict-origin-when-cross-origin";
                iframe.setAttribute("allowfullscreen", "");
                container.querySelector(".media-embed-consent").hidden = true;
                container.classList.remove("media-embed-pending");
                container.append(iframe);
            };

            const loadProvider = (provider) => {
                document.querySelectorAll(".media-embed").forEach((container) => {
                    if (container.dataset.provider === provider) loadVideo(container);
                });
            };

            const unloadProvider = (provider) => {
                document.querySelectorAll(".media-embed").forEach((container) => {
                    if (container.dataset.provider !== provider) return;
                    container.querySelector("iframe")?.remove();
                    container.querySelector(".media-embed-consent").hidden = false;
                    container.classList.add("media-embed-pending");
                });
            };

            document.querySelectorAll("[data-media-consent]").forEach((button) => {
                button.hidden = false;
            });

            readPermissions().forEach(loadProvider);

            document.getElementById("media-settings-open").addEventListener("click", () => {
                const permissions = readPermissions();
                providers.forEach((provider) => {
                    providerInputs.get(provider).checked = permissions.includes(provider);
                });
                settingsStatus.textContent = "";
                settingsDialog.showModal();
            });

            document.getElementById("media-settings-close").addEventListener("click", () => settingsDialog.close());

            document.getElementById("media-settings-save").addEventListener("click", () => {
                const otherPermissions = readPermissions().filter((provider) => !providers.includes(provider));
                const permissions = [
                    ...otherPermissions,
                    ...providers.filter((provider) => providerInputs.get(provider).checked)
                ];
                if (!savePermissions(permissions)) {
                    settingsStatus.textContent = "These settings could not be saved in this browser.";
                    return;
                }
                providers.forEach((provider) => {
                    if (permissions.includes(provider)) loadProvider(provider);
                    else unloadProvider(provider);
                });
                settingsDialog.close();
            });

            document.getElementById("media-settings-revoke").addEventListener("click", () => {
                if (!savePermissions([])) {
                    settingsStatus.textContent = "The saved settings could not be removed in this browser.";
                    return;
                }
                providers.forEach(unloadProvider);
                providers.forEach((provider) => { providerInputs.get(provider).checked = false; });
                settingsStatus.textContent = "All saved permissions have been removed, and loaded players have been unloaded.";
            });

            document.addEventListener("click", (event) => {
                if (!(event.target instanceof Element)) return;
                const button = event.target.closest("[data-media-consent]");
                if (!button) return;

                const container = button.closest(".media-embed");
                if (button.dataset.mediaConsent === "once") {
                    loadVideo(container);
                    return;
                }

                const permissions = readPermissions();
                if (!permissions.includes(container.dataset.provider)) permissions.push(container.dataset.provider);
                if (savePermissions(permissions)) loadProvider(container.dataset.provider);
                else {
                    const error = container.querySelector("[data-media-error]");
                    error.textContent = "This browser could not save the permission. You can still load the video once.";
                    error.hidden = false;
                }
            });

            window.addEventListener("storage", (event) => {
                if (event.key !== storageKey) return;
                const permissions = readPermissions();
                providers.forEach((provider) => {
                    if (permissions.includes(provider)) loadProvider(provider);
                    else unloadProvider(provider);
                });
            });
        })();
        """;

    public const string SettingsButtonHtml = """
        <button class="media-settings-toggle" id="media-settings-open" type="button" aria-label="Media" aria-haspopup="dialog" aria-controls="media-settings-dialog">
            <svg viewBox="0 0 20 20" aria-hidden="true" focusable="false">
                <path d="M8.2 2h3.6l.5 2a6.6 6.6 0 0 1 1.4.8l1.9-.7 1.8 3.1-1.5 1.4a6.8 6.8 0 0 1 0 1.7l1.5 1.4-1.8 3.1-1.9-.7a6.6 6.6 0 0 1-1.4.8l-.5 2H8.2l-.5-2a6.6 6.6 0 0 1-1.4-.8l-1.9.7-1.8-3.1 1.5-1.4a6.8 6.8 0 0 1 0-1.7L2.6 7.2l1.8-3.1 1.9.7a6.6 6.6 0 0 1 1.4-.8z"></path>
                <circle cx="10" cy="9.5" r="2.4"></circle>
            </svg>
        </button>
        """;

    public static string RenderSettingsDialog(IReadOnlyCollection<string> providers)
    {
        if (providers.Count == 0) return string.Empty;

        var providerOptions = string.Join(Environment.NewLine, providers.Select(provider =>
        {
            var providerId = provider == "YouTube" ? "youtube" : "vimeo";
            var encodedProvider = HtmlEncoder.Default.Encode(provider);
            return $"<label class=\"media-setting-option\"><input id=\"media-allow-{providerId}\" data-media-provider=\"{encodedProvider}\" type=\"checkbox\"> Allow {encodedProvider} videos on this site</label>";
        }));

        return $"""
            <dialog class="media-settings-dialog" id="media-settings-dialog" aria-labelledby="media-settings-title">
                <div class="media-settings-content">
                    <div class="search-dialog-heading">
                        <h2 id="media-settings-title">Media settings</h2>
                        <button class="search-close" id="media-settings-close" type="button" aria-label="Close media settings">×</button>
                    </div>
                    <p>Choose which providers may load videos automatically across this site. Your choices are stored in this browser.</p>
                    {providerOptions}
                    <p class="media-settings-status" id="media-settings-status" role="status" aria-live="polite"></p>
                    <div class="media-settings-actions">
                        <button class="media-settings-save" id="media-settings-save" type="button">Save settings</button>
                        <button class="media-settings-revoke" id="media-settings-revoke" type="button">Revoke all permissions</button>
                    </div>
                </div>
            </dialog>
            """;
    }

    public static string ReplaceStandaloneLinks(string html, out IReadOnlyCollection<string> providersUsed)
    {
        var providers = new HashSet<string>(StringComparer.Ordinal);
        var replaced = StandaloneLinkParagraphRegex.Replace(html, match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return match.Value;
            if (uri.Scheme is not ("http" or "https")) return match.Value;

            var (videoId, playerUrl, provider) = GetEmbed(uri);
            if (videoId is null) return match.Value;

            providers.Add(provider!);
            var encodedPlayerUrl = HtmlEncoder.Default.Encode(playerUrl!);
            var encodedOriginalUrl = HtmlEncoder.Default.Encode(uri.AbsoluteUri);
            var privacyPolicyUrl = provider == "YouTube"
                ? "https://policies.google.com/privacy"
                : "https://vimeo.com/privacy";
            var encodedPrivacyPolicyUrl = HtmlEncoder.Default.Encode(privacyPolicyUrl);
            var disclosure = $"Loading this video connects to {provider}. {provider} may process information about your visit.";
            var encodedProvider = HtmlEncoder.Default.Encode(provider!);
            var linkLabel = match.Groups["label"].Value;
            return $"""
                <div class="media-embed media-embed-pending" data-embed-url="{encodedPlayerUrl}" data-provider="{encodedProvider}">
                    <div class="media-embed-consent">
                        <p class="media-embed-label">{linkLabel}</p>
                        <p>{disclosure} Read the <a href="{encodedPrivacyPolicyUrl}">privacy policy</a>.</p>
                        <p class="media-embed-error" data-media-error hidden></p>
                        <div class="media-embed-actions">
                            <button type="button" class="media-embed-load" data-media-consent="once" hidden>Load this video once from {encodedProvider}</button>
                            <button type="button" class="media-embed-load media-embed-remember" data-media-consent="site" hidden>Always allow {encodedProvider} on this site</button>
                        </div>
                        <p>If you choose Always allow, you can change or revoke this permission later in Media settings.</p>
                    </div>
                    <noscript><p>JavaScript is disabled. <a href="{encodedOriginalUrl}">Open this video on {encodedProvider}</a>.</p></noscript>
                </div>
                """;
        });

        providersUsed = new[] { "YouTube", "Vimeo" }.Where(providers.Contains).ToArray();
        return replaced;
    }

    private static (string? VideoId, string? PlayerUrl, string? Provider) GetEmbed(Uri uri)
    {
        var host = uri.IdnHost.ToLowerInvariant();
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "youtu.be" or "www.youtu.be")
        {
            var videoId = host.EndsWith("youtu.be", StringComparison.Ordinal)
                ? segments.FirstOrDefault()
                : GetYouTubeVideoId(uri, segments);
            if (videoId is not null && YouTubeIdRegex.IsMatch(videoId))
                return (videoId, $"https://www.youtube-nocookie.com/embed/{videoId}", "YouTube");
        }

        if (host is "vimeo.com" or "www.vimeo.com")
        {
            var videoId = segments.FirstOrDefault();
            if (videoId is not null && VimeoIdRegex.IsMatch(videoId))
                return (videoId, $"https://player.vimeo.com/video/{videoId}?dnt=1", "Vimeo");
        }

        if (host == "player.vimeo.com" && segments.Length >= 2 && segments[0] == "video")
        {
            var videoId = segments[1];
            if (VimeoIdRegex.IsMatch(videoId))
                return (videoId, $"https://player.vimeo.com/video/{videoId}?dnt=1", "Vimeo");
        }

        return (null, null, null);
    }

    private static string? GetYouTubeVideoId(Uri uri, string[] segments)
    {
        if (uri.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var parameter in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = parameter.IndexOf('=');
                if (separator < 0) continue;
                if (Uri.UnescapeDataString(parameter[..separator]).Equals("v", StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(parameter[(separator + 1)..]);
            }
        }

        if (segments.Length >= 2 && segments[0] is "embed" or "shorts" or "live")
            return segments[1];

        return null;
    }
}
