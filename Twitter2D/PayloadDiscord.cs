using Imgur.API.Authentication;
using Imgur.API.Endpoints;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Tweetinvi.Models.V2;

namespace Twitter2D
{
    internal class PayloadDiscord
    {
        private class EntityContainer
        {
            public int Start { get; set; }
            public int End { get; set; }
            public string Replacement { get; set; }
        }

        public class Embed
        {
            public class Author
            {
                public string name { get; set; }
                public string icon_url { get; set; }
                public string url { get; set; }
            }

            public class Footer
            {
                public string text { get; set; }
                public string icon_url { get; set; }
            }

            public class Image
            {
                public string url { get; set; }
            }

            public string url { get; set; }
            public int color { get; set; }
            public string description { get; set; }
            public string title { get; set; }
            public Author author { get; set; }
            public Footer footer { get; set; }
            public Image image { get; set; }
        }

        [JsonProperty("username")]
        public string Username { get; }

        [JsonProperty("avatar_url")]
        public string Avatar { get; }

        [JsonProperty("content")]
        public string Content { get; private set; }

        [JsonProperty("embeds")]
        public List<Embed> Embeds { get; } = new();

        public PayloadDiscord(TweetV2 tweet, UserV2 author, string tweetUrl, string DisplayName)
        {
            if (!String.IsNullOrWhiteSpace(DisplayName))
            {
                Username = DisplayName;
            }
            else
            {
                Username = author.Username;
            }

            Avatar = author.ProfileImageUrl;

            // TODO: Escape markdown
            FormatTweetAsync(tweet, author, tweetUrl);
        }

        private void FormatTweetAsync(TweetV2 tweet, UserV2 author, string tweetUrl)
        {
            try
            {
                string AuthorInfo = $"@{author.Username}";

                var text = tweet.Text;
                var entities = new List<EntityContainer>();
                var images = new List<Embed>();
                bool isGIF = false;
                string GIFImageURL = "";
                bool isMP4 = false;
                string MP4URL = "";

                if (Program.TwitterCustomMessages.TryGetValue(tweet.AuthorId, out string Message))
                {
                    Content = Message;
                }

                if (tweet.Entities.Hashtags != null)
                {
                    foreach (var entity in tweet.Entities.Hashtags)
                    {
                        if (!entities.Exists(x => x.Start == entity.Start))
                        {
                            entities.Add(new EntityContainer
                            {
                                Start = entity.Start,
                                End = entity.End,
                                Replacement = $"[#{entity.Tag}](https://twitter.com/hashtag/{entity.Tag})"
                            });
                        }
                    }
                }

                if (tweet.Entities.Mentions != null)
                {
                    foreach (var entity in tweet.Entities.Mentions)
                    {
                        if (!entities.Exists(x => x.Start == entity.Start))
                        {
                            entities.Add(new EntityContainer
                            {
                                Start = entity.Start,
                                End = entity.End,
                                Replacement = $"[@{entity.Username}](https://twitter.com/{entity.Username})"
                            });
                        }
                    }
                }

                if (tweet.Attachments.MediaKeys != null)
                {
                    var TweetResponse = Program.userClient.TweetsV2.GetTweetAsync(tweet.Id).Result;
                    foreach (var entity in tweet.Entities.Urls)
                    {
                        if (entity.DisplayUrl.Contains("pic.twitter.com"))
                        {
                            // Remove the short url from text
                            entity.ExpandedUrl = "";

                            if (!entities.Exists(x => x.Start == entity.Start))
                            {
                                entities.Add(new EntityContainer
                                {
                                    Start = entity.Start,
                                    End = entity.End,
                                    Replacement = "",
                                });
                            }
                        }
                    }

                    foreach (MediaV2 MediaItem in TweetResponse.Includes.Media)
                    {
                        if (MediaItem.Type == "animated_gif")
                        {
                            isGIF = true;
                            string MP4Code = MediaItem.PreviewImageUrl.Replace("https://pbs.twimg.com/tweet_video_thumb/", "").Replace(".jpg", "");
                            string GIFMP4URL = "https://video.twimg.com/tweet_video/" + MP4Code + ".mp4";
                            //https://pbs.twimg.com/tweet_video/NDJLDjZC2cWXtRkV.mp4
                            //NDJLDjZC2cWXtRkV
                            ProcessStartInfo FFMPEGInfo = new ProcessStartInfo()
                            {
                                FileName = "ffmpeg",
                                Arguments = "-i " + GIFMP4URL + " " + tweet.Id + ".gif",
                                UseShellExecute = true,
                                WindowStyle = ProcessWindowStyle.Hidden,
                                CreateNoWindow = true
                            };
                            Process.Start(FFMPEGInfo).WaitForExit();
                            var apiClient = new ApiClient(Program.config.ImgurToken);
                            var httpClient = new HttpClient();

                            var filePath = tweet.Id + ".gif";
                            using var fileStream = File.OpenRead(filePath);

                            var imageEndpoint = new ImageEndpoint(apiClient, httpClient);
                            var imageUpload = imageEndpoint.UploadImageAsync(fileStream);
                            GIFImageURL = imageUpload.Result.Link;
                            File.Delete(tweet.Id + ".gif");
                        }
                        else if (MediaItem.Type == "video")
                        {
                            if (!Program.TwitterCustomMessages.TryGetValue(tweet.AuthorId, out Message))
                            {
                                Content = tweetUrl;
                            }
                            else
                            {
                                Content = Message + tweetUrl;
                            }
                            return;
                        }
                        else
                        {
                            images.Add(new Embed
                            {
                                url = tweetUrl,
                                image = new Embed.Image
                                {
                                    url = MediaItem.Url
                                },
                            });
                        }
                    }
                }

                if (tweet.Entities?.Urls != null)
                {
                    foreach (var entity in tweet.Entities.Urls)
                    {
                        if (entity.DisplayUrl.Contains("pic.twitter.com")) { continue; }
                        if (!entities.Exists(x => x.Start == entity.Start))
                        {
                            entities.Add(new EntityContainer
                            {
                                Start = entity.Start,
                                End = entity.End,
                                Replacement = entity.ExpandedUrl,
                            });
                        }
                    }
                }

                if (entities.Any())
                {
                    entities = entities.OrderBy(e => e.Start).ToList();

                    var charIndex = 0;
                    var entityIndex = 0;
                    var codePointIndex = 0;
                    var entityCurrent = entities[0];

                    while (charIndex < text.Length)
                    {
                        if (entityCurrent.Start == codePointIndex)
                        {
                            var len = entityCurrent.End - entityCurrent.Start;
                            entityCurrent.Start = charIndex;
                            entityCurrent.End = charIndex + len;

                            entityIndex++;

                            if (entityIndex == entities.Count)
                            {
                                // no more entity
                                break;
                            }

                            entityCurrent = entities[entityIndex];
                        }

                        if (charIndex < text.Length - 1 && char.IsSurrogatePair(text[charIndex], text[charIndex + 1]))
                        {
                            // Found surrogate pair
                            charIndex++;
                        }

                        codePointIndex++;
                        charIndex++;
                    }

                    foreach (var entity in entities.OrderByDescending(e => e.Start))
                    {
                        text = text[..entity.Start] + entity.Replacement + text[entity.End..];
                    }
                }

                text = WebUtility.HtmlDecode(text);

                var embed = new Embed
                {
                    url = tweetUrl,
                    color = 1941746,
                    description = text/*.Replace(tweetUrl + "/photo/1","")*/, //hacky, but it works
                    title = $"{author.Name}",
                    author = new Embed.Author
                    {
                        name = AuthorInfo,
                        icon_url = author.ProfileImageUrl,
                        url = tweetUrl,
                    },
                    footer = new Embed.Footer
                    {
                        text = "Tweet created at " + tweet.CreatedAt.ToString("G") + " UTC",
                        icon_url = "https://i.imgur.com/RJku9A1.png"
                    }
                };

                if (isGIF)
                {
                    embed.image = new Embed.Image
                    {
                        url = GIFImageURL
                    };
                }
                else if(isMP4)
                {
                    embed.image = new Embed.Image
                    {
                        url = MP4URL
                    };
                }
                else
                {
                    if (images.Any())
                    {
                        embed.image = images[0].image;
                        images.RemoveAt(0);
                    }
                }

                Embeds.Add(embed);
                Embeds.AddRange(images);
            }
            catch (Exception f)
            {
                Console.WriteLine("Something went wrong - " + f.ToString());
            }
        }
    }
}