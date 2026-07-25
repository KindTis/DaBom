using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dabom.Library;

public sealed class LibraryStore
{
    private const long MaxPosterBytes = 10_485_760;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly HashSet<string> PosterExtensions = new(
        [".jpg", ".jpeg", ".png", ".bmp"], StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, string, CancellationToken, Task> _commit;
    private readonly Action<string> _deletePoster;
    private readonly Func<string, CancellationToken, Task<string?>>
        _getPosterExtension;

    public LibraryStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dabom")) { }

    internal LibraryStore(
        string rootPath,
        Func<string, string, CancellationToken, Task>? commit = null,
        Action<string>? deletePoster = null,
        Func<string, CancellationToken, Task<string?>>? getPosterExtension = null)
    {
        RootPath = Path.GetFullPath(rootPath);
        JsonPath = Path.Combine(RootPath, "library.json");
        PostersPath = Path.Combine(RootPath, "posters");
        _commit = commit ?? CommitFileAsync;
        _deletePoster = deletePoster ?? File.Delete;
        _getPosterExtension =
            getPosterExtension ?? PosterImage.TryGetExtensionAsync;
    }

    internal string RootPath { get; }
    internal string JsonPath { get; }
    internal string PostersPath { get; }
    public bool CanSave { get; private set; } = true;
    public string? LoadWarning { get; private set; }

    public async Task<LibraryData> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(RootPath);
            Directory.CreateDirectory(PostersPath);
            FileStream stream;
            try
            {
                stream = File.OpenRead(JsonPath);
            }
            catch (FileNotFoundException)
            {
                return new();
            }
            catch (DirectoryNotFoundException)
            {
                return new();
            }

            try
            {
                await using (stream)
                {
                    var data = await JsonSerializer.DeserializeAsync<LibraryData>(
                        stream, JsonOptions, cancellationToken)
                        ?? throw new JsonException("library.json 루트가 null입니다.");
                    return Normalize(data);
                }
            }
            catch (Exception error) when (error is JsonException or NotSupportedException
                or ArgumentException or PathTooLongException)
            {
                try
                {
                    var backup = Path.Combine(
                        RootPath, $"library.corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
                    File.Copy(JsonPath, backup, overwrite: false);
                    LoadWarning = $"손상된 library.json을 {backup}에 백업하고 빈 라이브러리로 시작했습니다.";
                }
                catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException)
                {
                    CanSave = false;
                    LoadWarning = $"손상된 library.json을 백업하지 못해 저장을 비활성화했습니다: {JsonPath}";
                }

                return new();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            CanSave = false;
            LoadWarning = $"라이브러리 데이터를 읽지 못해 저장을 비활성화했습니다: {JsonPath} — {error.Message}";
            return new();
        }
    }

    public async Task SaveAsync(LibraryData data, CancellationToken cancellationToken = default)
    {
        if (!CanSave)
        {
            throw new InvalidOperationException("라이브러리 데이터를 안전하게 읽지 못해 저장이 비활성화되었습니다.");
        }

        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(PostersPath);
        var temporary = Path.Combine(RootPath, $"library.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, Normalize(data), JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            await _commit(temporary, JsonPath, cancellationToken);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    public async Task SaveAsync(
        LibraryData data,
        string? createdPoster,
        CancellationToken cancellationToken)
    {
        try
        {
            await SaveAsync(data, cancellationToken);
        }
        catch
        {
            try { DeletePoster(createdPoster); }
            catch (Exception error) when (
                error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public async Task<string> DownloadPosterAsync(
        HttpClient client,
        Uri source,
        CancellationToken cancellationToken)
    {
        if (!source.IsAbsoluteUri || source.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("포스터 주소는 HTTPS여야 합니다.");
        }

        using var response = await client.GetAsync(
            source,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxPosterBytes)
        {
            throw new InvalidDataException("포스터가 10 MiB 제한을 넘습니다.");
        }

        Directory.CreateDirectory(PostersPath);
        var temporary = Path.Combine(
            PostersPath,
            $".{Guid.NewGuid():N}.tmp");
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            await using (var output = new FileStream(
                temporary,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None))
            {
                var buffer = new byte[81_920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(
                    buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxPosterBytes)
                    {
                        throw new InvalidDataException(
                            "포스터가 10 MiB 제한을 넘습니다.");
                    }
                    await output.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var extension = await _getPosterExtension(
                temporary,
                cancellationToken)
                ?? throw new InvalidDataException(
                    "지원하지 않거나 손상된 포스터입니다.");
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = $"{Guid.NewGuid():D}{extension}";
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, Path.Combine(PostersPath, fileName));
            return Path.Combine("posters", fileName).Replace('\\', '/');
        }
        catch (Exception error)
        {
            try { File.Delete(temporary); }
            catch (Exception cleanupError) when (
                cleanupError is IOException or UnauthorizedAccessException) { }
            if (error is OperationCanceledException
                && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            throw;
        }
    }

    public async Task<string> ImportPosterAsync(
        string videoPath,
        string selectedImagePath,
        CancellationToken cancellationToken = default)
    {
        _ = Path.GetFullPath(videoPath);
        var source = Path.GetFullPath(selectedImagePath);
        var extension = Path.GetExtension(source);
        if (!PosterExtensions.Contains(extension) || PosterImage.TryLoad(source) is null)
        {
            throw new InvalidDataException("JPG, JPEG, PNG 또는 BMP 이미지 파일을 선택하세요.");
        }

        Directory.CreateDirectory(PostersPath);
        var fileName = $"{Guid.NewGuid():D}{extension.ToLowerInvariant()}";
        var destination = Path.Combine(PostersPath, fileName);
        try
        {
            await using var input = File.OpenRead(source);
            await using var output = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return Path.Combine("posters", fileName).Replace('\\', '/');
        }
        catch
        {
            try { File.Delete(destination); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public string? ResolvePosterPath(string? relativePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(Path.Combine(
                RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return fullPath.StartsWith(
                PostersPath + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
                    ? fullPath
                    : null;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public void DeletePoster(string? relativePath)
    {
        var path = ResolvePosterPath(relativePath);
        if (path is not null)
        {
            _deletePoster(path);
        }
    }

    private static Task CommitFileAsync(string temporary, string destination, CancellationToken _)
    {
        if (File.Exists(destination))
        {
            File.Replace(temporary, destination, null);
        }
        else
        {
            File.Move(temporary, destination);
        }

        return Task.CompletedTask;
    }

    private static LibraryData Normalize(LibraryData data)
    {
        if (data.Locations is null || data.VideosByPath is null
            || data.VideosByPath.Any(pair => pair.Value is null))
        {
            throw new JsonException("library.json 필수 컬렉션이 올바르지 않습니다.");
        }

        var locations = data.Locations
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var videos = new Dictionary<string, VideoRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in data.VideosByPath)
        {
            var path = Path.GetFullPath(pair.Key);
            videos[path] = NormalizeRecord(path, pair.Value);
        }

        return data with { Locations = locations, VideosByPath = videos };
    }

    private static VideoRecord NormalizeRecord(string path, VideoRecord record)
    {
        var status = record.MetadataStatus;
        if (status == MetadataStatus.Unspecified)
        {
            var fileTitle = Path.GetFileNameWithoutExtension(path);
            var hasManualValue =
                !string.IsNullOrWhiteSpace(record.OriginalTitle)
                || record.ReleaseDate is not null
                || !string.IsNullOrWhiteSpace(record.Director)
                || record.Actors is { Length: > 0 }
                || !string.IsNullOrWhiteSpace(record.Synopsis)
                || !string.IsNullOrWhiteSpace(record.Poster)
                || (!string.IsNullOrWhiteSpace(record.Title)
                    && !record.Title.Trim().Equals(
                        fileTitle, StringComparison.Ordinal));
            status = hasManualValue
                ? MetadataStatus.Manual
                : MetadataStatus.Pending;
        }

        return record with
        {
            Actors = record.Actors ?? [],
            Genres = record.Genres ?? [],
            ProviderReferences = record.ProviderReferences ?? [],
            UserEditedFields = record.UserEditedFields ?? [],
            MetadataStatus = status
        };
    }
}
