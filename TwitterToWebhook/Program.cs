using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tweetinvi;
using Tweetinvi.Events.V2;
using Tweetinvi.Models.V2;
using Tweetinvi.Parameters.V2;
using Tweetinvi.Streaming.V2;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwitterStreaming
{
    class Program : IDisposable
    {
        private readonly Dictionary<string, List<Uri>> TwitterToWebhooks = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> TwitterToDisplayName = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, string> TwitterCustomMessages = new(StringComparer.OrdinalIgnoreCase);
        private readonly HttpClient HttpClient;
        private IFilteredStreamV2 TwitterStream;
        public static TwitterClient userClient;
        public static TwitterConfig config;

        public Program()
        {
            HttpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "TwitterToWebhook");
            HttpClient.Timeout = TimeSpan.FromSeconds(20);
        }

        public void Dispose()
        {
            HttpClient?.Dispose();
        }

        public async Task Initialize()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            config = JsonSerializer.Deserialize<TwitterConfig>(await File.ReadAllTextAsync(path), new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });


            Log.WriteError("Test error");

            userClient = new TwitterClient(config.ConsumerKey, config.ConsumerSecret, config.BearerToken);

            TwitterStream = userClient.StreamsV2.CreateFilteredStream();

            foreach (var (_, webhooks) in config.AccountsToMonitor)
            {
                foreach (var webhook in webhooks)
                {
                    if (!config.WebhookUrls.ContainsKey(webhook))
                    {
                        Log.WriteError($"Webhook \"{webhook}\" does not exist in WebhookUrls.");
                    }
                }
            }

            var twitterUsers = await userClient.UsersV2.GetUsersByNameAsync(config.AccountsToMonitor.Keys.ToArray());

            var followers = new List<FilteredStreamRuleConfig>();

            int WebHookCount = 0;
            int DisplayNamesCount = 0;
            int CustomMessagesCount = 0;

            foreach (var user in twitterUsers.Users)
            {
                var webhooks = config.AccountsToMonitor.First(u => u.Key.Equals(user.Username, StringComparison.OrdinalIgnoreCase));

                Log.WriteInfo($"Following @{user.Username} ({user.Id})");
                if (config.DisplayNames.ContainsKey(user.Username))
                {
                    TwitterToWebhooks.Add(user.Id, webhooks.Value.Select(x => config.WebhookUrls[x]).ToList());
                    WebHookCount++;
                }
                else
                {
                    Log.WriteError("Couldn't find a corresponding webhook for @" + user.Username + "! Please check your config and restart the program. Press any key to continue.");
                    Console.ReadKey();
                    Environment.Exit(0);
                }
                if (config.DisplayNames.ContainsKey(user.Username))
                {
                    TwitterToDisplayName.Add(user.Id, config.DisplayNames.First(u => u.Key.Equals(user.Username, StringComparison.OrdinalIgnoreCase)).Value);
                    DisplayNamesCount++;
                }
                if (config.CustomMessages.ContainsKey(user.Username))
                {
                    TwitterCustomMessages.Add(user.Id, config.CustomMessages.First(u => u.Key.Equals(user.Username, StringComparison.OrdinalIgnoreCase)).Value);
                    CustomMessagesCount++;
                }
                followers.Add(new FilteredStreamRuleConfig($"from:{user.Id}"));
            }

            Log.WriteInfo("Loaded " + WebHookCount + " webhook(s), " + DisplayNamesCount + " display name(s), and " + CustomMessagesCount + " custom message(s).");

            var rules = await userClient.StreamsV2.GetRulesForFilteredStreamV2Async();

            if (rules.Rules.Length > 0)
            {
                Log.WriteInfo($"Deleting {rules.Rules.Length} existing rules");
                await userClient.StreamsV2.DeleteRulesFromFilteredStreamAsync(rules.Rules);
            }

            await userClient.StreamsV2.AddRulesToFilteredStreamAsync(followers.ToArray());
        }

        public async Task StartTwitterStream()
        {
            TwitterStream.TweetReceived += OnTweetReceived;

            var parameters = new StartFilteredStreamV2Parameters();
            parameters.AddCustomQueryParameter("expansions", "referenced_tweets.id");

            do
            {
                try
                {
                    Log.WriteInfo("Connecting to stream");
                    await TwitterStream.StartAsync();
                }
                catch (Exception ex)
                {
                    Log.WriteError($"Exception caught: {ex}");
                }

                await Task.Delay(10000);
            }
            while (true);
        }

        private async void OnTweetReceived(object sender, FilteredStreamTweetV2EventArgs matchedTweetReceivedEventArgs)
        {
            var tweet = matchedTweetReceivedEventArgs.Tweet;

            if (tweet == null)
            {
                if (matchedTweetReceivedEventArgs.Json.Contains("Too Many Requests"))
                {
                    TwitterStream.StopStream();
                    TwitterStream.TweetReceived -= OnTweetReceived;
                    Log.WriteError($"We're sending too many requests! Let's try again in like... 5 minutes?");
                    Thread.Sleep(300000);
                    Log.WriteInfo("Let's try again!");
                    TwitterStream.TweetReceived += OnTweetReceived;
                    await StartTwitterStream();
                }
                else if(matchedTweetReceivedEventArgs.Json.Contains("TooManyConnections"))
                {
                    TwitterStream.StopStream();
                    TwitterStream.TweetReceived -= OnTweetReceived;
                    Log.WriteError($"We have too many concurrent connections?? Let's try again in like... 2 minutes?");
                    Thread.Sleep(120000);
                    Log.WriteInfo("Let's try again!");
                    TwitterStream.TweetReceived += OnTweetReceived;
                    await StartTwitterStream();
                }
                else
                {
                    Log.WriteError($"Failed to receive tweet: {matchedTweetReceivedEventArgs.Json}");
                }
                return;
            }

            var author = matchedTweetReceivedEventArgs.Includes.Users.First(user => user.Id == tweet.AuthorId);
            var url = $"https://twitter.com/{author.Username}/status/{tweet.Id}";

            if (tweet.ReferencedTweets != null || tweet.InReplyToUserId != null)
            {
                Log.WriteInfo($"@{author.Username} ({tweet.AuthorId}) (Reply/Retweet, skipped): {url}");
            }

            // Skip tweets from accounts that are not monitored (quirk of how twitter streaming works)
            // TODO: Probably not needed in v2
            if (!TwitterToWebhooks.TryGetValue(tweet.AuthorId, out var endpoints))
            {
                Log.WriteInfo($"@{author.Username} ({tweet.AuthorId}) (skipped): {url}");
                return;
            }

            if (!TwitterToDisplayName.TryGetValue(tweet.AuthorId, out string DisplayName))
            {
                DisplayName = "";
            }

            Log.WriteInfo($"@{author.Username} ({tweet.AuthorId}) tweeted: ({tweet.Id}) {url}");

            foreach (var hookUrl in endpoints)
            {
                await SendWebhook(hookUrl, tweet, author, url, DisplayName);
            }
        }

        private async Task SendWebhook(Uri url, TweetV2 tweet, UserV2 author, string tweetUrl, string DisplayName)
        {
            string json;

            if (url.Host == "discord.com")
            {
                // If webhook target is Discord, convert it to a Discord compatible payload
                json = JsonConvert.SerializeObject(new PayloadDiscord(tweet, author, tweetUrl, DisplayName));
            }
            else
            {
                json = JsonConvert.SerializeObject(new PayloadGeneric(tweet, author, tweetUrl));
            }

            Console.WriteLine(json);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            try
            {
                var result = await HttpClient.PostAsync(url, content);
                var output = await result.Content.ReadAsStringAsync();

                Log.WriteInfo($"Webhook result ({(int)result.StatusCode}): {output}");
            }
            catch (Exception e)
            {
                Log.WriteError($"Webhook exception: {e}");
            }
        }
    }
}
