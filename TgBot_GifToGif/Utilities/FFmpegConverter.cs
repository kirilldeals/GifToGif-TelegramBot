using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TgBot_GifToGif.Utilities
{
    public class FFmpegConverter
    {
        private readonly string _ffmpegExePath;

        private FFmpegConverter(string ffmpegExePath)
        {
            _ffmpegExePath = ffmpegExePath;
        }

        public static async Task<FFmpegConverter> CreateAsync(string ffmpegDir)
        {
            if (!Directory.Exists(ffmpegDir))
            {
                Directory.CreateDirectory(ffmpegDir);
            }
            var ffmpegExePath = Directory.EnumerateFiles(ffmpegDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();

            if (ffmpegExePath == null)
            {
                string ffmpegUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
                string ffmpegZipFile = "ffmpeg-release-essentials.zip";

                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromMinutes(10);
                    HttpResponseMessage response = await client.GetAsync(ffmpegUrl);
                    response.EnsureSuccessStatusCode();
                    using (Stream responseStream = await response.Content.ReadAsStreamAsync())
                    {
                        using (FileStream fileStream = File.Create(ffmpegZipFile))
                        {
                            responseStream.CopyTo(fileStream);
                        }
                    }
                }

                ZipFile.ExtractToDirectory(ffmpegZipFile, ffmpegDir);
                File.Delete(ffmpegZipFile);

                ffmpegExePath = Directory.EnumerateFiles(ffmpegDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (ffmpegExePath == null)
                {
                    throw new Exception("ffmpeg.exe not found.");
                }
            }

            return new FFmpegConverter(ffmpegExePath);
        }

        public async Task<string> ConvertMP4ToGIF(string sourceFilePath, string destinationDirectory, bool deleteSourceFile = false)
        {
            string gifFilePath = $"{Path.Combine(destinationDirectory, Path.GetFileNameWithoutExtension(sourceFilePath))}.gif";
            string rootDrive = $"{Path.GetPathRoot(_ffmpegExePath)}".Replace("\\", "");
            string ffmpegDir = Path.GetDirectoryName(_ffmpegExePath);
            string parameters = $"ffmpeg -y -i \"{sourceFilePath}\" -filter_complex \"[0:v] split[a][b];[a] palettegen [p];[b][p] paletteuse\" \"{gifFilePath}\"";

            using (Process p = new Process())
            {
                p.StartInfo.FileName = "cmd.exe";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardInput = true;
                p.Start();
                using (StreamWriter sw = p.StandardInput)
                {
                    if (sw.BaseStream.CanWrite)
                    {
                        await sw.WriteLineAsync(rootDrive);
                        await sw.WriteLineAsync($"cd {ffmpegDir}");
                        await sw.WriteLineAsync(parameters);
                    }
                }
                p.WaitForExit();
            }

            if (deleteSourceFile)
            {
                File.Delete(sourceFilePath);
            }
            return gifFilePath;
        }
    }
}
