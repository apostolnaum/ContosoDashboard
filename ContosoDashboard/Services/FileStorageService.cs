namespace ContosoDashboard.Services;

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default);
    Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default);
    Task<string> GetUrlAsync(string filePath, int? expirySeconds = null);
    Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default);
}

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _storageRoot;

    public LocalFileStorageService()
    {
        _storageRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ContosoDashboard",
            "uploads");
        Directory.CreateDirectory(_storageRoot);
    }

    public async Task<string> UploadAsync(Stream fileStream, string fileName, CancellationToken cancellationToken = default)
    {
        var relativePath = Path.Combine(
            DateTime.UtcNow.ToString("yyyy/MM/dd"),
            $"{Guid.NewGuid():N}_{SanitizeFileName(fileName)}");

        var fullPath = Path.Combine(_storageRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await fileStream.CopyToAsync(output, cancellationToken);

        return relativePath;
    }

    public Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_storageRoot, filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Document file not found.", fullPath);

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task<string> GetUrlAsync(string filePath, int? expirySeconds = null)
    {
        // For local storage, return a relative app path used by the download endpoint
        return Task.FromResult($"/api/documents/file/{Uri.EscapeDataString(filePath)}");
    }

    public Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(_storageRoot, filePath);
        if (!File.Exists(fullPath))
            return Task.FromResult(false);

        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(fileName.Select(c => invalid.Contains(c) ? '_' : c));
        return safe.Length > 100 ? safe[..100] : safe;
    }
}
