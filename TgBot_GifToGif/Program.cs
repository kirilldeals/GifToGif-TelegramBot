using System;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Threading;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types;
using Telegram.Bot;
using System.IO;
using TgBot_GifToGif.Models;
using System.Reflection.Metadata;
using Telegram.Bot.Types.InputFiles;
using static System.Net.WebRequestMethods;
using TgBot_GifToGif.Utilities;
using System.IO.Compression;
using System.Diagnostics;

namespace TgBot_GifToGif
{
    internal class Program
    {
        private static FFmpegConverter _converter;

        static async Task Main(string[] args)
        {
            Console.WriteLine("Getting FFmpeg executable file...");
            while (true)
            {
                try
                {
                    _converter = await FFmpegConverter.CreateAsync(Path.Combine(Directory.GetCurrentDirectory(), "ffmpeg"));
                    Console.WriteLine("FFmpeg is ready!");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false)
                .Build();

            var settings = config
                .GetRequiredSection("BotSettings")
                .Get<BotSettings>();

            var botClient = new TelegramBotClient(settings.BotToken);

            using CancellationTokenSource cts = new();

            botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                cancellationToken: cts.Token
            );

            var me = await botClient.GetMeAsync();

            Console.WriteLine($"Start listening for @{me.Username}");
            Console.ReadLine();

            cts.Cancel();
        }

        async static Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message)
                return;
            var chatId = message.Chat.Id;
            try
            {
                if (message.Text is { } messageText)
                {
                    if (messageText.ToLower() == "/start")
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "*This bot turns Telegram GIFs into actual .gif files*\nJust send your saved GIF or any .mp4 file.",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                        var user = message.Chat.Username != null ? $"@{message.Chat.Username}" : $"{message.From.Id}";
                        Console.WriteLine($"User {user} joined bot");
                    }
                    return;
                }

                if (message.Document is { } document)
                {
                    string docFileName = document.FileName ?? $"gif{DateTime.Now.Ticks}.mp4";
                    if (document.MimeType != "video/mp4")
                    {
                        await botClient.SendTextMessageAsync(
                            chatId: chatId,
                            text: "The document should be in .mp4 format",
                            cancellationToken: cancellationToken);
                        return;
                    }
                    Console.WriteLine($"Received a document: '{docFileName}' from the chat {chatId}.");

                    var waitingMessage = await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Converting...",
                        cancellationToken: cancellationToken);

                    // Download and convert mp4 file
                    var localFilePath = Path.Combine(Path.GetTempPath(), docFileName);
                    await using (Stream fileStream = System.IO.File.OpenWrite(localFilePath))
                    {
                        var file = await botClient.GetInfoAndDownloadFileAsync(
                            fileId: document.FileId,
                            destination: fileStream,
                            cancellationToken: cancellationToken);
                    }
                    var gifPath = await _converter.ConvertMP4ToGIF(localFilePath, Path.GetDirectoryName(localFilePath), true);
                    var gifFileName = Path.GetFileName(gifPath);
                    var gifFileNameWithoutExtension = Path.GetFileNameWithoutExtension(gifPath);

                    //Archive file
                    string archiveFilePath = Path.Combine(Path.GetTempPath(), $"{DateTime.Now.Ticks}{gifFileNameWithoutExtension}.zip");
                    using (ZipArchive archive = ZipFile.Open(archiveFilePath, ZipArchiveMode.Create))
                    {
                        archive.CreateEntryFromFile(gifPath, gifFileName);
                    }

                    //Send archive
                    using (Stream stream = new FileStream(archiveFilePath, FileMode.Open))
                    {
                        InputOnlineFile inputOnlineFile = new InputOnlineFile(stream);
                        inputOnlineFile.FileName = $"{gifFileNameWithoutExtension}.zip";
                        await botClient.SendDocumentAsync(
                            chatId: chatId, 
                            document: inputOnlineFile,
                            cancellationToken: cancellationToken);
                    }
                    System.IO.File.Delete(gifPath);
                    System.IO.File.Delete(archiveFilePath);

                    await botClient.DeleteMessageAsync(
                        chatId: chatId,
                        messageId: waitingMessage.MessageId,
                        cancellationToken: cancellationToken);

                    Console.WriteLine($"File {gifFileName} was archived and sent to the chat {chatId}.");
                }
            }
            catch (ApiRequestException ex)
            {
                Console.WriteLine($"{ex.Message} (chat ID: {chatId})");
            }
        }

        static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }
    }
}