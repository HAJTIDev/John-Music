#nullable enable
using DSharpPlus.SlashCommands;
using DSharpPlus.Entities;
using Lavalink4NET;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SpotifyAPI.Web;
namespace DiscordBotTemplate.Commands
{
    public class Basic : ApplicationCommandModule
    {
        private readonly IAudioService _audioService;

        public Basic(IAudioService audioService)
        {
            ArgumentNullException.ThrowIfNull(audioService);
            _audioService = audioService;
        }

        [SlashCommand("play", "Odtwórz muzykę")]
        public async Task PlayMusic(InteractionContext ctx, [Option("query", "query")] string query)
        {
            await ctx.DeferAsync().ConfigureAwait(false);
            var userVc = ctx.Member.VoiceState?.Channel;
            var player = await GetPlayerAsync(ctx, connectToVoiceChannel: true).ConfigureAwait(false);

            if (player is null)
            {
                var response = new DiscordFollowupMessageBuilder().WithContent("❌ **Something went wrong!**");
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
                return;
            }
            if (userVc == null)
            {
                var response = new DiscordFollowupMessageBuilder().WithContent("❌ **You have to be in a voice call to do that!**");
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
                return;
            }
            if (query.Contains("spotify.com/album/"))
            {
                await SpotiAlbumConvert(query, ctx);
                return;
            }
            if (query.Contains("spotify.com/playlist/"))
            {
                await SpotiPlaylistConvert(query, ctx);
                return;
            }
            var track = await _audioService.Tracks
                .LoadTrackAsync(query, TrackSearchMode.YouTube)
                .ConfigureAwait(false);
            if (track == null)
            {
                var response = new DiscordFollowupMessageBuilder().WithContent($"❌ **Nothing found for query:** `{query}`");
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
                return;
            }
            var position = await player
                .PlayAsync(track)
                .ConfigureAwait(false);

            if (position is 0)
            {
                var embed = new DiscordEmbedBuilder()
                {
                    Title = "🎶 **Now Playing**",
                    Description = $"[{track.Title}]({track.Uri})\n🎤 **Author:** {track.Author}\n",
                    Color = DiscordColor.Azure
                };
                var response = new DiscordFollowupMessageBuilder().AddEmbed(embed);
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
            }
            else
            {
                var embed = new DiscordEmbedBuilder()
                {
                    Title = "🎶 **Added to Queue**",
                    Description = $"[{track.Title}]({track.Uri})\n🎤 **Author:** {track.Author}\n",
                    Color = DiscordColor.Azure
                };
                var response = new DiscordFollowupMessageBuilder().AddEmbed(embed);
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
            }
        }
        private async Task SpotiAlbumConvert(string query, InteractionContext ctx)
        {
            using var client = new HttpClient();
            var credentials = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "2dd5ec93e1804c258730dad8b0b29898",
                ["client_secret"] = "24346387fe9646519beab80f47ca1b56"
            };

            try
            {
                var response = await client.PostAsync(
                    "https://accounts.spotify.com/api/token",
                    new FormUrlEncodedContent(credentials));
                var content = await response.Content.ReadAsStringAsync();
                var token = Regex.Match(content, "\"access_token\":\"([^\"]+)\"").Groups[1].Value;

                if (string.IsNullOrEmpty(token))
                {
                    var resp = new DiscordFollowupMessageBuilder().WithContent("❌ Failed to get Spotify token");
                    await ctx.FollowUpAsync(resp).ConfigureAwait(false);
                    return;
                }
                var spotify = new SpotifyClient(token);
                var albumId = query.Split("/").Last().Split("?").First();
                var resp2 = new DiscordFollowupMessageBuilder().WithContent("⏳ Importing album from Spotify...");
                await ctx.FollowUpAsync(resp2).ConfigureAwait(false);
                var album = await spotify.Albums.Get(albumId);
                if (album?.Tracks == null)
                {
                    var resp3 = new DiscordFollowupMessageBuilder().WithContent("❌ Could not load album tracks");
                    await ctx.FollowUpAsync(resp3).ConfigureAwait(false);
                    return;
                }
                var player = await GetPlayerAsync(ctx, connectToVoiceChannel: true).ConfigureAwait(false);
                if (player == null)
                {
                    var resp3 = new DiscordFollowupMessageBuilder().WithContent("❌ Could not create player");
                    await ctx.FollowUpAsync(resp3).ConfigureAwait(false);
                    return;
                }
                var importedTracks = 0;
                if (album.Tracks?.Items != null)
                {
                    foreach (var item in album.Tracks.Items)
                    {
                        if (item is SimpleTrack track)
                        {
                            try
                            {
                                var SearchTrack = await _audioService.Tracks
                                    .LoadTrackAsync($"{track.Artists[0].Name} - {track.Name}", TrackSearchMode.YouTube)
                                    .ConfigureAwait(false);
                                if (SearchTrack != null)
                                {
                                    var position = await player
                                        .PlayAsync(SearchTrack)
                                        .ConfigureAwait(false);
                                    importedTracks++;
                                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"✅ Imported {track.Artists[0].Name} - {track.Name}!"));
                                }
                            }
                            catch { continue; }
                        }
                    }
                }
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"✅ Imported {importedTracks} tracks from Spotify album!"));
            }
            catch (Exception ex)
            {
                var response = new DiscordFollowupMessageBuilder().WithContent($"❌ Error: {ex.Message}");
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
            }
        }
        private async Task SpotiPlaylistConvert(string query, InteractionContext ctx)
        {
            using var client = new HttpClient();
            var credentials = new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "2dd5ec93e1804c258730dad8b0b29898",
                ["client_secret"] = "24346387fe9646519beab80f47ca1b56"
            };

            try
            {
                var response = await client.PostAsync(
                    "https://accounts.spotify.com/api/token",
                    new FormUrlEncodedContent(credentials));
                var content = await response.Content.ReadAsStringAsync();
                var token = Regex.Match(content, "\"access_token\":\"([^\"]+)\"").Groups[1].Value;

                if (string.IsNullOrEmpty(token))
                {
                    var resp = new DiscordFollowupMessageBuilder().WithContent("❌ Failed to get Spotify token");
                    await ctx.FollowUpAsync(resp).ConfigureAwait(false);
                    return;
                }
                var spotify = new SpotifyClient(token);
                var playlistId = query.Split("/").Last().Split("?").First();
                var resp2 = new DiscordFollowupMessageBuilder().WithContent("⏳ Importing playlist from Spotify...");
                await ctx.FollowUpAsync(resp2).ConfigureAwait(false);
                var playlist = await spotify.Playlists.Get(playlistId);
                if (playlist?.Tracks == null)
                {
                    var resp3 = new DiscordFollowupMessageBuilder().WithContent("❌ Could not load playlist tracks");
                    await ctx.FollowUpAsync(resp3).ConfigureAwait(false);
                    return;
                }
                var player = await GetPlayerAsync(ctx, connectToVoiceChannel: true).ConfigureAwait(false);
                if (player == null)
                {
                    var resp3 = new DiscordFollowupMessageBuilder().WithContent("❌ Could not create player");
                    await ctx.FollowUpAsync(resp3).ConfigureAwait(false);
                    return;
                }
                var importedTracks = 0;
                if (playlist.Tracks?.Items != null)
                {
                    foreach (var item in playlist.Tracks.Items)
                    {
                        if (item.Track is FullTrack track)
                        {
                            try
                            {
                                var SearchTrack = await _audioService.Tracks
                                    .LoadTrackAsync($"{track.Artists[0].Name} - {track.Name}", TrackSearchMode.YouTube)
                                    .ConfigureAwait(false);
                                if (SearchTrack != null)
                                {
                                    var position = await player
                                        .PlayAsync(SearchTrack)
                                        .ConfigureAwait(false);
                                    importedTracks++;
                                    await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"✅ Imported {track.Artists[0].Name} - {track.Name}!"));
                                }
                            }
                            catch { continue; }
                        }
                    }
                }
                await ctx.EditResponseAsync(new DiscordWebhookBuilder().WithContent($"✅ Imported {importedTracks} tracks from Spotify playlist!"));
            }
            catch (Exception ex)
            {
                var response = new DiscordFollowupMessageBuilder().WithContent($"❌ Error: {ex.Message}");
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
            }
        }

        private async ValueTask<QueuedLavalinkPlayer?> GetPlayerAsync(InteractionContext interactionContext, bool connectToVoiceChannel = true)
        {
            ArgumentNullException.ThrowIfNull(interactionContext);

            var retrieveOptions = new PlayerRetrieveOptions(
                ChannelBehavior: connectToVoiceChannel ? PlayerChannelBehavior.Join : PlayerChannelBehavior.None);

            var playerOptions = new QueuedLavalinkPlayerOptions { HistoryCapacity = 10000 };

            var result = await _audioService.Players
                .RetrieveAsync(interactionContext.Guild.Id, interactionContext.Member?.VoiceState.Channel.Id, playerFactory: PlayerFactory.Queued, Options.Create(playerOptions), retrieveOptions)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                var errorMessage = result.Status switch
                {
                    PlayerRetrieveStatus.UserNotInVoiceChannel => "You are not connected to a voice channel.",
                    PlayerRetrieveStatus.BotNotConnected => "The bot is currently not connected.",
                    _ => "Unknown error.",
                };

                var errorResponse = new DiscordFollowupMessageBuilder()
                    .WithContent(errorMessage)
                    .AsEphemeral();

                await interactionContext
                    .FollowUpAsync(errorResponse)
                    .ConfigureAwait(false);

                return null;
            }

            return result.Player;
        }

        [SlashCommand("loop", "Toggle track loop")]
        public async Task Loop(InteractionContext ctx, [Choice("Track", "Track")][Choice("Queue", "Queue")][Choice("None", "None")][Option("type","type")] string looptype)
        {
            var player = await GetPlayerAsync(ctx, connectToVoiceChannel: false).ConfigureAwait(false);
            await ctx.DeferAsync().ConfigureAwait(false);

            if (player == null)
            {
                var response = new DiscordFollowupMessageBuilder().WithContent("❌ **Not currently playing anything**");
                await ctx.FollowUpAsync(response);
                return;
            }
            if (looptype == "Track")
            {
                player.RepeatMode = TrackRepeatMode.Track;
                var response = new DiscordFollowupMessageBuilder().WithContent($"🔁 **Track loop** is now **{(player.RepeatMode)}**");
                await ctx.FollowUpAsync(response);
            }
            else if (looptype == "Queue")
            {
                player.RepeatMode = TrackRepeatMode.Queue;
                var response = new DiscordFollowupMessageBuilder().WithContent($"🔁 **Queue loop** is now **{(player.RepeatMode)}**");
                await ctx.FollowUpAsync(response);
            }
            else if (looptype == "None")
            {
                player.RepeatMode = TrackRepeatMode.None;
                var response = new DiscordFollowupMessageBuilder().WithContent($"🔁 **Looping** is now **disabled**");
                await ctx.FollowUpAsync(response);
            }
        }
        [SlashCommand("shuffle", "Przetasuj kolejkę")]
        public async Task Shuffle(InteractionContext ctx)
        {
            var player = await GetPlayerAsync(ctx, connectToVoiceChannel: false).ConfigureAwait(false);
            await ctx.DeferAsync().ConfigureAwait(false);

            if (player == null)
            {
                var response = new DiscordFollowupMessageBuilder().WithContent("❌ **Not currently playing anything**");
                await ctx.FollowUpAsync(response);
                return;
            }

                player.Shuffle = !player.Shuffle;
                var Sresponse = new DiscordFollowupMessageBuilder().WithContent("🔀 **Queue shuffle toggled**");
                await ctx.FollowUpAsync(Sresponse);
                return;
        }

        [SlashCommand("queue", "Pokaż kolejkę utworów")]
        public async Task ShowQueue(InteractionContext ctx)
        {
            await ctx.DeferAsync().ConfigureAwait(false);
            var player = await GetPlayerAsync(ctx, connectToVoiceChannel: false).ConfigureAwait(false);
            if (player != null && player.Queue.Count > 0)
            {
                var trackList = player.Queue
                    .Where(track => track.Track != null)
                    .Select((track, index) => track.Track != null ? $"{index + 1}. [{track.Track.Title}]({track.Track.Uri}) - 🎤 {track.Track.Author}" : string.Empty)
                    .Where(trackInfo => !string.IsNullOrEmpty(trackInfo))
                    .ToList();
                var embed = new DiscordEmbedBuilder()
                {
                    Title = "📜 **Track list**",
                    Description = string.Join("\n", trackList),
                    Color = DiscordColor.Gold
                };
                var response = new DiscordFollowupMessageBuilder().AddEmbed(embed);
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
            }
            else
            {
                var embed = new DiscordEmbedBuilder()
                {
                    Title = "❌ **Queue is empty!**",
                    Description = "Add new songs with `/play`. ",
                    Color = DiscordColor.Red
                };
                var response = new DiscordFollowupMessageBuilder().AddEmbed(embed);
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
            }
        }

        [SlashCommand("skip", "Pomiń utwór")]
        public async Task Skip(InteractionContext ctx)
        {
            var player = await GetPlayerAsync(ctx, connectToVoiceChannel: false).ConfigureAwait(false);
            await ctx.DeferAsync().ConfigureAwait(false);

            if (player == null)
            {
                var response = new DiscordFollowupMessageBuilder().WithContent("❌ **Not currently playing anything**");
                await ctx.FollowUpAsync(response);
                return;
            }

            if (player.CurrentTrack != null)
            {
                await player.SkipAsync().ConfigureAwait(true);
                var response = new DiscordFollowupMessageBuilder().WithContent("⏭️ **Skipping track...**");
                await ctx.FollowUpAsync(response);
            }
            else
            {
                var embed = new DiscordEmbedBuilder()
                {
                    Title = "❌ **Queue is empty!**",
                    Description = "Nothing left to play.",
                    Color = DiscordColor.Red
                };
                var response = new DiscordFollowupMessageBuilder().AddEmbed(embed);
                await ctx.FollowUpAsync(response).ConfigureAwait(false);
            }
        }

        [SlashCommand("help", "Show all available commands")]
        public static async Task ShowHelp(InteractionContext ctx)
        {
            await ctx.DeferAsync().ConfigureAwait(false);
            var embed = new DiscordEmbedBuilder()
            {
                Title = "🎵 **Available Commands**",
                Description = "Here are the commands you can use:",
                Color = DiscordColor.Azure
            };

            embed.AddField("▶️ `/play`", "Play music or add a track to the queue.", false);
            embed.AddField("📜 `/queue`", "Show the current music queue.", false);
            embed.AddField("⏭️ `/skip`", "Skip the current track.", false);
            embed.AddField("❔ `/help`", "Show the list of available commands.", false);
            embed.AddField("❌ `/clear`", "Clears queue and stops the current track.", false);
            embed.AddField("👋 `/leave`", "Leave the voice channel.", false);

            var response = new DiscordFollowupMessageBuilder().AddEmbed(embed);
            await ctx.FollowUpAsync(response).ConfigureAwait(false);
        }


        [SlashCommand("clear", "Stop the music and clear the queue")]
        public async Task ClearQueue(InteractionContext ctx)
        {
            await ctx.DeferAsync().ConfigureAwait(false);
            var player = await GetPlayerAsync(ctx, connectToVoiceChannel: false).ConfigureAwait(false);
            if (player != null && player.Queue.Count > 0)
            {
                await player.Queue.ClearAsync();
                await player.StopAsync();
                var embed = new DiscordEmbedBuilder()
                {
                    Title = "🗑️ **Queue cleared**",
                    Description = "The queue has been cleared and the current track has been stopped.",
                    Color = DiscordColor.Red
             