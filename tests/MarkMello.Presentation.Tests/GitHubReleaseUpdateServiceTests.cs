using MarkMello.Application.Updates;
using MarkMello.Infrastructure.Updates;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace MarkMello.Presentation.Tests;

public sealed class GitHubReleaseUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsyncReturnsUpdateAvailableForCurrentRuntimeAsset()
    {
        var assetName = GetCurrentRuntimeAssetName();
        if (assetName is null)
        {
            return;
        }

        var service = CreateService(
            $$"""
            {
              "tag_name": "v1.2.3",
              "name": "v1.2.3",
              "html_url": "https://github.com/kostyatab/Softmark/releases/tag/v1.2.3",
              "published_at": "2026-04-19T12:00:00Z",
              "assets": [
                {
                  "name": "{{assetName}}",
                  "browser_download_url": "https://github.com/kostyatab/Softmark/releases/download/v1.2.3/{{assetName}}",
                  "state": "uploaded"
                }
              ]
            }
            """);

        var result = await service.CheckForUpdatesAsync();

        var available = Assert.IsType<UpdateCheckResult.UpdateAvailable>(result);
        Assert.Equal("1.0.0", available.Package.CurrentVersion);
        Assert.Equal("1.2.3", available.Package.ReleaseVersion);
        Assert.Equal(assetName, available.Package.AssetName);
    }

    [Fact]
    public async Task CheckForUpdatesAsyncReturnsUpToDateWhenVersionsMatch()
    {
        var assetName = GetCurrentRuntimeAssetName();
        if (assetName is null)
        {
            return;
        }

        var service = CreateService(
            $$"""
            {
              "tag_name": "v1.0.0",
              "name": "v1.0.0",
              "html_url": "https://github.com/kostyatab/Softmark/releases/tag/v1.0.0",
              "published_at": "2026-04-19T12:00:00Z",
              "assets": [
                {
                  "name": "{{assetName}}",
                  "browser_download_url": "https://github.com/kostyatab/Softmark/releases/download/v1.0.0/{{assetName}}",
                  "state": "uploaded"
                }
              ]
            }
            """);

        var result = await service.CheckForUpdatesAsync();

        var upToDate = Assert.IsType<UpdateCheckResult.UpToDate>(result);
        Assert.Equal("1.0.0", upToDate.CurrentVersion);
        Assert.Equal("1.0.0", upToDate.LatestVersion);
    }

    /// <summary>
    /// Сервер назвал размер: ход загрузки идёт от нуля до полного размера, и доля в конце — 1.
    /// </summary>
    [Fact]
    public async Task DownloadReportsProgressAgainstTheKnownLength()
    {
        using var directory = new TemporaryDirectory();
        var payload = new byte[200_000];
        var service = CreateDownloadService(new ByteArrayContent(payload), directory.Path);
        var progress = new RecordingProgress();

        var result = await service.DownloadUpdateAsync(CreatePackage(), progress);

        var success = Assert.IsType<UpdateDownloadResult.Success>(result);
        Assert.Equal(payload.Length, new FileInfo(success.DownloadedFilePath).Length);
        Assert.Equal(new UpdateDownloadProgress(0, payload.Length), progress.Reports[0]);
        Assert.Equal(new UpdateDownloadProgress(payload.Length, payload.Length), progress.Reports[^1]);
        Assert.True(progress.Reports.Count > 2);
        Assert.Equal(1, progress.Reports[^1].Fraction);
        Assert.False(File.Exists(success.DownloadedFilePath + ".download"));
    }

    /// <summary>Без размера доля неизвестна, но полученные байты всё равно считаются.</summary>
    [Fact]
    public async Task DownloadReportsBytesWithoutFractionWhenTheLengthIsUnknown()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateDownloadService(new UnknownLengthContent(new byte[1000]), directory.Path);
        var progress = new RecordingProgress();

        var result = await service.DownloadUpdateAsync(CreatePackage(), progress);

        Assert.IsType<UpdateDownloadResult.Success>(result);
        Assert.All(progress.Reports, static report => Assert.Null(report.TotalBytes));
        Assert.All(progress.Reports, static report => Assert.Null(report.Fraction));
        Assert.Equal(1000, progress.Reports[^1].BytesReceived);
    }

    /// <summary>
    /// Отмена бросает <see cref="OperationCanceledException"/> и не оставляет в папке ни
    /// недокачанного <c>.download</c>, ни готового файла.
    /// </summary>
    [Fact]
    public async Task CancelledDownloadLeavesNoPartialFile()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var service = CreateDownloadService(new StalledContent(), directory.Path);
        var progress = new RecordingProgress(report =>
        {
            if (report.BytesReceived > 0)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DownloadUpdateAsync(CreatePackage(), progress, cancellation.Token));

        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    /// <summary>Обрыв посреди загрузки — ошибка в результате, и недокачанный файл тоже удалён.</summary>
    [Fact]
    public async Task FailedDownloadLeavesNoPartialFile()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateDownloadService(new StalledContent(failInsteadOfWaiting: true), directory.Path);

        var result = await service.DownloadUpdateAsync(CreatePackage());

        Assert.IsType<UpdateDownloadResult.Failed>(result);
        Assert.Empty(Directory.GetFiles(directory.Path));
    }

    /// <summary>
    /// «Нет сети» — только когда до GitHub не дошли: исключение сети или таймаут HttpClient.
    /// Ответ с ошибкой (например, 403 при исчерпанном лимите) — не проблема связи.
    /// </summary>
    [Theory]
    [InlineData("network", true)]
    [InlineData("timeout", true)]
    [InlineData("forbidden", false)]
    public async Task CheckFailureTellsConnectionProblemsFromServiceErrors(string failure, bool expected)
    {
        if (GetCurrentRuntimeAssetName() is null)
        {
            return;
        }

        HttpMessageHandler handler = failure switch
        {
            "network" => new ThrowingHttpMessageHandler(new HttpRequestException("No route to host")),
            "timeout" => new ThrowingHttpMessageHandler(new TaskCanceledException("The request timed out.")),
            _ => new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Forbidden))
        };
        var service = new GitHubReleaseUpdateService(new HttpClient(handler), typeof(GitHubReleaseUpdateServiceTests).Assembly);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(expected, Assert.IsType<UpdateCheckResult.Failed>(result).IsConnectionProblem);
    }

    /// <summary>Скачанного файла нет — отдельный ответ, а не общая ошибка: его можно скачать заново.</summary>
    [Fact]
    public async Task PreparingAMissingDownloadReportsFileNotFound()
    {
        using var directory = new TemporaryDirectory();
        var service = CreateDownloadService(new ByteArrayContent([]), directory.Path);
        var missing = Path.Combine(directory.Path, "Softmark-test.dmg");

        var result = await service.PrepareDownloadedUpdateAsync(CreatePackage(), missing);

        Assert.Equal(missing, Assert.IsType<UpdatePrepareResult.FileNotFound>(result).Path);
    }

    private static GitHubReleaseUpdateService CreateDownloadService(HttpContent content, string downloadDirectory)
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        return new GitHubReleaseUpdateService(
            new HttpClient(handler),
            typeof(GitHubReleaseUpdateServiceTests).Assembly,
            downloadDirectory);
    }

    private static AppUpdatePackage CreatePackage()
        => new(
            CurrentVersion: "1.0.0",
            ReleaseVersion: "1.2.3",
            ReleaseTag: "v1.2.3",
            PublishedAt: DateTimeOffset.UnixEpoch,
            ReleasePageUrl: "https://github.com/kostyatab/Softmark/releases/tag/v1.2.3",
            AssetName: "Softmark-test.dmg",
            DownloadUrl: "https://github.com/kostyatab/Softmark/releases/download/v1.2.3/Softmark-test.dmg",
            PlatformName: "macOS",
            ArchitectureName: "arm64",
            InstallAction: AppUpdateInstallAction.OpenDiskImage);

    private static GitHubReleaseUpdateService CreateService(string responseJson)
    {
        var handler = new StubHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MarkMello.Tests/updates");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        return new GitHubReleaseUpdateService(client, typeof(GitHubReleaseUpdateServiceTests).Assembly);
    }

    private static string? GetCurrentRuntimeAssetName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "Softmark-setup-win-x64.exe",
                Architecture.Arm64 => "Softmark-setup-win-arm64.exe",
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "Softmark-macos-x64.dmg",
                Architecture.Arm64 => "Softmark-macos-arm64.dmg",
                _ => null
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "Softmark-linux-x86_64.AppImage",
                Architecture.Arm64 => "Softmark-linux-aarch64.AppImage",
                _ => null
            };
        }

        return null;
    }

    /// <summary>Записывает отчёты сразу, в том же потоке: у <c>Progress&lt;T&gt;</c> они уходили бы в пул.</summary>
    private sealed class RecordingProgress(Action<UpdateDownloadProgress>? onReport = null) : IProgress<UpdateDownloadProgress>
    {
        public List<UpdateDownloadProgress> Reports { get; } = [];

        public void Report(UpdateDownloadProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    /// <summary>Ответ без Content-Length — как у сервера, который отдаёт файл кусками.</summary>
    private sealed class UnknownLengthContent(byte[] payload) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => stream.WriteAsync(payload).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new MemoryStream(payload));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>Отдаёт первый кусок и зависает до отмены — или обрывается, если так попросили.</summary>
    private sealed class StalledContent(bool failInsteadOfWaiting = false) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
            => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new StalledStream(failInsteadOfWaiting));

        protected override bool TryComputeLength(out long length)
        {
            length = 1_000_000;
            return true;
        }
    }

    private sealed class StalledStream(bool failInsteadOfWaiting) : Stream
    {
        private bool _firstChunkSent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_firstChunkSent)
            {
                _firstChunkSent = true;
                buffer.Span[..100].Clear();
                return 100;
            }

            if (failInsteadOfWaiting)
            {
                throw new IOException("Connection reset.");
            }

            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "softmark-update-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }
}
