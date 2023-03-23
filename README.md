# Twitter2D (Twitter to Discord)

This program listens to Twitter stream and forwards tweets to a webhook url, typically using a discord embed. Discord webhooks are natively supported.

See `config_example.json` file for configuration.

It also uses [Imgur](http://www.imgur.com) to support GIF tweets and [FFmpeg](https://ffmpeg.org/) to support MP4 tweets.

You can get a free Imgur API key [here](https://api.imgur.com/oauth2/addclient) and then just make sure the ffmpeg binaries are accessible from the command prompt/bash and/or in the same place as the compiled DLL.

Current optional parameters:
* Changing the display name for the webhook user
* Sending a custom text message above the embed
* Ignoring tweets that don't have a specified keyword in them
* Sending error messages to a webhook

[Original TwitterToWebhook example by xPaw](https://github.com/xPaw/TwitterToWebhook)
