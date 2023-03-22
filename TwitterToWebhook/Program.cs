using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json;
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
        private readonly Dictionary<string, List<Uri>> TwitterToChannels = new();
        private readonly Dictionary<string, string> TwitterToDisplayName = new();
        private readonly HttpClient HttpClient;
        private IFilteredStreamV2 TwitterStream;
        public static TwitterClient userClient;

        public Program()
        {
            HttpClient = new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
            HttpClient.DefaultRequestHeaders.Add("User-Agent", "TwitterToWebhook");
            HttpClient.Timeout = TimeSpan.FromSeconds(10);
        }

        public void Dispose()
        {
            HttpClient?.Dispose();
        }

        public async Task Initialize()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            var config = JsonSerializer.Deserialize<TwitterConfig>(await File.ReadAllTextAsync(path), new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            userClient = new TwitterClient(config.ConsumerKey, config.ConsumerSecret, config.BearerToken);

            TwitterStream = userClient.StreamsV2.CreateFilteredStream();

            foreach (var (_, channels) in config.AccountsToMonitor)
            {
                foreach (var channel in channels)
                {
                    if (!config.WebhookUrls.ContainsKey(channel))
                    {
                        throw new KeyNotFoundException($"Channel \"{channel}\" does not exist in WebhookUrls.");
                    }
                }
            }

            var twitterUsers = await userClient.UsersV2.GetUsersByNameAsync(config.AccountsToMonitor.Keys.ToArray());

            var followers = new List<FilteredStreamRuleConfig>();

            foreach (var user in twitterUsers.Users)
            {
                var channels = config.AccountsToMonitor.First(u => u.Key.Equals(user.Username, StringComparison.OrdinalIgnoreCase));

                Log.WriteInfo($"Following @{user.Username} ({user.Id})");

                TwitterToChannels.Add(user.Id, channels.Value.Select(x => config.WebhookUrls[x]).ToList());

                TwitterToDisplayName.Add(user.Id, config.DisplayNames.First(u => u.Key.Equals(user.Username, StringComparison.OrdinalIgnoreCase)).Value);

                followers.Add(new FilteredStreamRuleConfig($"from:{user.Id}"));
            }

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

                await Task.Delay(5000);
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
                    Log.WriteError($"We're sending too many requests! Let's try again in like... 5 minutes?");
                    await Task.Delay(300000);
                    Log.WriteInfo("Let's try again!");
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
            if (!TwitterToChannels.TryGetValue(tweet.AuthorId, out var endpoints))
            {
                Log.WriteInfo($"@{author.Username} ({tweet.AuthorId}) (skipped): {url}");
                return;
            }

            if (!TwitterToDisplayName.TryGetValue(tweet.AuthorId, out string DisplayName))
            {
                DisplayName = "";
            }

            //// Skip replies unless replying to another monitored account
            //if (tweet.InReplyToUserId != null && !TwitterToChannels.ContainsKey(tweet.InReplyToUserId))
            //{
            //    Log.WriteInfo($"@{author.Username} ({tweet.AuthorId}) replied to @_ ({tweet.InReplyToUserId}): {url}");
            //    return;
            //}

            //// When retweeting a monitored account, do not send retweets to channels that original tweeter also sends to
            //if (tweet.ReferencedTweets != null)
            //{
            //    foreach (var referencedTweet in tweet.ReferencedTweets)
            //    {
            //        if (referencedTweet.Type == "retweeted")
            //        {
            //            var referencedTweetData = matchedTweetReceivedEventArgs.Includes.Tweets.FirstOrDefault(x => x.Id == referencedTweet.Id);
            //            var authorRetweeted = matchedTweetReceivedEventArgs.Includes.Users.First(user => user.Id == referencedTweetData.AuthorId);
            //            var urlRetweeted = $"https://twitter.com/{authorRetweeted.Username}/status/{referencedTweet.Id}";

            //            if (referencedTweetData != null && TwitterToChannels.TryGetValue(referencedTweetData.AuthorId, out var retweetEndpoints))
            //            {
            //                endpoints = endpoints.Except(retweetEndpoints).ToList();

            //                if (!endpoints.Any())
            //                {
            //                    Log.WriteInfo($"@{author.Username} ({tweet.AuthorId}) retweeted @{authorRetweeted.Username} ({referencedTweetData.AuthorId}): {urlRetweeted}");
            //                    return;
            //                }
            //            }

            //            author = authorRetweeted;
            //            url = urlRetweeted;

            //            break;
            //        }
            //    }
            //}

#if false
            // When quote-tweeting a monitored account, do not embed quoted tweet to channels that original tweeter also sends to
            var ignoreQuoteTweet = tweet.QuotedTweet != null
                && TwitterToChannels.TryGetValue(tweet.QuotedTweet.CreatedBy.Id, out var quoteTweetEndpoints)
                && !endpoints.Except(quoteTweetEndpoints).Any();
#endif

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
