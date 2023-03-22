using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using Newtonsoft.Json;
using Tweetinvi.Core.Models;
using Tweetinvi.Models.V2;

namespace TwitterStreaming
{
    class PayloadDiscord
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

        //public PayloadDiscord(TweetV2 tweet, UserV2 author, string url, bool ignoreQuoteTweet)
        //{
        //    Username = $"New Tweet by @{author.Username}";
        //    Avatar = author.ProfileImageUrl;
        //    Content = url;
        //}
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
            FormatTweet(tweet, author, tweetUrl);
        }

        private void FormatTweet(TweetV2 tweet, UserV2 author, string tweetUrl)
        {
            try
            {
                string AuthorInfo = $"{author.Name} (@{author.Username})";

                var text = tweet.Text;
                var entities = new List<EntityContainer>();
                var images = new List<Embed>();

                //if (tweet.Entities?.Urls != null)
                //{
                //    foreach (var entity in tweet.Entities.Urls)
                //    {
                //        if (!entities.Exists(x => x.Start == entity.Indices[0]))
                //        {
                //            entities.Add(new EntityContainer
                //            {
                //                Start = entity.Indices[0],
                //                End = entity.Indices[1],
                //                Replacement = entity.ExpandedURL,
                //            });
                //        }
                //    }
                //}

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

                //if (tweet.Attachments.MediaKeys.Count() > 0)
                //{
                //    foreach (var entity in tweet.Attachments.MediaKeys)
                //    {
                //        //if (entity.MediaType is "photo" or "animated_gif" or "video")
                //        //{
                //        //    images.Add(new Embed
                //        //    {
                //        //        url = tweet.Url,
                //        //        image = new Embed.Image
                //        //        {
                //        //            url = entity.MediaURLHttps,
                //        //        },
                //        //    });

                //        //    if (entity.MediaType is "photo" or "animated_gif")
                //        //    {
                //        //        // Remove the short url from text
                //        //        entity.ExpandedURL = "";
                //        //    }
                //        //}

                //        //if (!entities.Exists(x => x.Start == entity.Indices[0]))
                //        //{
                //        //    entities.Add(new EntityContainer
                //        //    {
                //        //        Start = entity.Indices[0],
                //        //        End = entity.Indices[1],
                //        //        Replacement = entity.ExpandedURL,
                //        //    });
                //        //}
                //    }
                //}

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
                    description = text,
                    author = new Embed.Author
                    {
                        name = AuthorInfo,
                        icon_url = author.ProfileImageUrl,
                        url = tweetUrl,
                    },
                };

                if (images.Any())
                {
                    embed.image = images[0].image;
                    images.RemoveAt(0);
                }

                Embeds.Add(embed);
                Embeds.AddRange(images);
            }
            catch(Exception f)
            {
                Console.WriteLine("Something went wrong - " + f.ToString());
            }
        }
    }
}
