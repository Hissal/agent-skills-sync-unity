using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Hissal.AgentSkillsSync
{
    /// <summary>Downloads a URL to a file. The network edge of <see cref="GitHubSkillFetcher"/>.</summary>
    public interface IArchiveDownloader
    {
        /// <exception cref="Exception">Any failure; the fetcher reports it as a <see cref="SkillFetchException"/>.</exception>
        void Download(string url, string destinationPath);
    }

    /// <summary>Downloads over HTTPS, blocking the caller.</summary>
    public sealed class HttpArchiveDownloader : IArchiveDownloader
    {
        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        public void Download(string url, string destinationPath)
        {
            // Task.Run keeps the continuations off the caller's synchronization context (Unity's main thread).
            Task.Run(async () =>
            {
                using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new IOException($"{url} returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
                    using (var file = File.Create(destinationPath))
                        await response.Content.CopyToAsync(file).ConfigureAwait(false);
                }
            }).GetAwaiter().GetResult();
        }
    }
}
