# Service Contract: IFileStorageService

**Feature**: Document Upload and Management  
**Layer**: Infrastructure Abstraction (Service Layer)  
**Purpose**: Decouple document storage from business logic, enabling cloud migration  
**Status**: Design Phase

## Interface: IFileStorageService

```csharp
namespace ContosoDashboard.Services;

/// <summary>
/// Abstraction for file storage operations.
/// Enables swapping between local filesystem (training) and cloud (production) implementations.
/// </summary>
public interface IFileStorageService {
  /// <summary>
  /// Uploads a file stream to storage.
  /// </summary>
  /// <param name="fileStream">File content to store</param>
  /// <param name="filePath">Target path (e.g., "42/1001/550e8400-e29b-41d4-a716-446655440000.pdf")</param>
  /// <param name="cancellationToken">Cancellation token</param>
  /// <returns>Relative/absolute path where file was stored (for database record)</returns>
  /// <exception cref="IOException">If storage fails (disk full, permissions, etc.)</exception>
  /// <remarks>
  /// Implementations:
  /// - LocalFileStorageService: Saves to AppData/uploads/{filePath}
  /// - AzureBlobStorageService: Uploads to blob container with same {filePath} as blob name
  /// 
  /// Path Format: {userId}/{projectId or "personal"}/{GUID}.{extension}
  /// Example: 42/1001/550e8400-e29b-41d4-a716-446655440000.pdf
  /// </remarks>
  Task<string> UploadAsync(Stream fileStream, string filePath, CancellationToken cancellationToken = default);

  /// <summary>
  /// Downloads a file from storage as a stream.
  /// </summary>
  /// <param name="filePath">Path to the file (as stored in database)</param>
  /// <param name="cancellationToken">Cancellation token</param>
  /// <returns>File stream (caller responsible for disposing)</returns>
  /// <exception cref="FileNotFoundException">If file does not exist</exception>
  /// <exception cref="UnauthorizedAccessException">If access denied (permissions)</exception>
  Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default);

  /// <summary>
  /// Retrieves a public/secure URL for the file.
  /// Used by preview endpoints and download links.
  /// </summary>
  /// <param name="filePath">Path to the file</param>
  /// <param name="expiresInMinutes">Optional expiration (for cloud implementations with SAS tokens)</param>
  /// <returns>URL that can be embedded in HTML or used for downloads</returns>
  /// <remarks>
  /// Implementations:
  /// - LocalFileStorageService: Returns internal controller endpoint URL
  ///   e.g., "/api/documents/42/preview?path=..."
  /// - AzureBlobStorageService: Returns SAS URL with expiration
  ///   e.g., "https://storageaccount.blob.core.windows.net/documents/...?sv=2021-06-08&..."
  /// </remarks>
  Task<string> GetUrlAsync(string filePath, int? expiresInMinutes = null);

  /// <summary>
  /// Permanently deletes a file from storage.
  /// </summary>
  /// <param name="filePath">Path to file to delete</param>
  /// <param name="cancellationToken">Cancellation token</param>
  /// <returns>True if deleted; false if file did not exist</returns>
  /// <exception cref="UnauthorizedAccessException">If permissions denied</exception>
  Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default);
}
```

## Implementation: LocalFileStorageService (Training)

```csharp
namespace ContosoDashboard.Services;

/// <summary>
/// Local filesystem-based file storage.
/// Suitable for training environments.
/// Files stored in AppData/uploads/ directory outside wwwroot.
/// </summary>
public class LocalFileStorageService : IFileStorageService {
  private readonly string _uploadDirectory;
  private readonly ILogger<LocalFileStorageService> _logger;

  public LocalFileStorageService(IConfiguration config, ILogger<LocalFileStorageService> logger) {
    // Resolve upload directory from config
    var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    _uploadDirectory = Path.Combine(appDataPath, "ContosoDashboard", "uploads");
    _logger = logger;
    
    // Ensure directory exists
    Directory.CreateDirectory(_uploadDirectory);
  }

  public async Task<string> UploadAsync(Stream fileStream, string filePath, CancellationToken cancellationToken = default) {
    var fullPath = Path.Combine(_uploadDirectory, filePath);
    var directory = Path.GetDirectoryName(fullPath);
    
    // Create subdirectories if needed (e.g., AppData/uploads/42/1001/)
    Directory.CreateDirectory(directory);
    
    try {
      using (var fileToWrite = System.IO.File.Create(fullPath)) {
        await fileStream.CopyToAsync(fileToWrite, cancellationToken);
      }
      _logger.LogInformation($"File uploaded: {filePath}");
      return fullPath;
    } catch (Exception ex) {
      _logger.LogError($"Upload failed: {filePath}, Error: {ex.Message}");
      throw new IOException($"Failed to upload file: {ex.Message}", ex);
    }
  }

  public async Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default) {
    var fullPath = Path.Combine(_uploadDirectory, filePath);
    
    if (!System.IO.File.Exists(fullPath)) {
      throw new FileNotFoundException($"File not found: {filePath}");
    }
    
    try {
      // Return FileStream with buffer (allows multiple reads)
      return System.IO.File.OpenRead(fullPath);
    } catch (UnauthorizedAccessException) {
      throw;
    } catch (Exception ex) {
      _logger.LogError($"Download failed: {filePath}, Error: {ex.Message}");
      throw;
    }
  }

  public Task<string> GetUrlAsync(string filePath, int? expiresInMinutes = null) {
    // Return controller endpoint URL
    // Actual URL determined by base address at runtime
    return Task.FromResult($"/api/documents/download?path={Uri.EscapeDataString(filePath)}");
  }

  public async Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default) {
    var fullPath = Path.Combine(_uploadDirectory, filePath);
    
    if (!System.IO.File.Exists(fullPath)) {
      return false;
    }
    
    try {
      System.IO.File.Delete(fullPath);
      _logger.LogInformation($"File deleted: {filePath}");
      return true;
    } catch (Exception ex) {
      _logger.LogError($"Delete failed: {filePath}, Error: {ex.Message}");
      throw new IOException($"Failed to delete file: {ex.Message}", ex);
    }
  }
}
```

## Future Implementation: AzureBlobStorageService (Production)

```csharp
namespace ContosoDashboard.Services;

/// <summary>
/// Azure Blob Storage-based file storage.
/// Suitable for production deployments.
/// </summary>
public class AzureBlobStorageService : IFileStorageService {
  private readonly BlobContainerClient _containerClient;
  private readonly ILogger<AzureBlobStorageService> _logger;

  public AzureBlobStorageService(BlobContainerClient containerClient, ILogger<AzureBlobStorageService> logger) {
    _containerClient = containerClient;
    _logger = logger;
  }

  public async Task<string> UploadAsync(Stream fileStream, string filePath, CancellationToken cancellationToken = default) {
    try {
      var blobClient = _containerClient.GetBlobClient(filePath);
      await blobClient.UploadAsync(fileStream, overwrite: true, cancellationToken);
      _logger.LogInformation($"Blob uploaded: {filePath}");
      return filePath;
    } catch (Exception ex) {
      _logger.LogError($"Upload failed: {filePath}, Error: {ex.Message}");
      throw new IOException($"Failed to upload to Azure: {ex.Message}", ex);
    }
  }

  public async Task<Stream> DownloadAsync(string filePath, CancellationToken cancellationToken = default) {
    try {
      var blobClient = _containerClient.GetBlobClient(filePath);
      var download = await blobClient.DownloadAsync(cancellationToken);
      return download.Value.Content;
    } catch (Azure.RequestFailedException ex) when (ex.Status == 404) {
      throw new FileNotFoundException($"Blob not found: {filePath}");
    } catch (Exception ex) {
      _logger.LogError($"Download failed: {filePath}, Error: {ex.Message}");
      throw;
    }
  }

  public async Task<string> GetUrlAsync(string filePath, int? expiresInMinutes = null) {
    try {
      var blobClient = _containerClient.GetBlobClient(filePath);
      
      if (expiresInMinutes.HasValue) {
        // Generate SAS URL with expiration
        var sasUri = blobClient.GenerateSasUri(
          BlobSasPermissions.Parse("r"),
          DateTimeOffset.UtcNow.AddMinutes(expiresInMinutes.Value)
        );
        return sasUri.AbsoluteUri;
      }
      
      return blobClient.Uri.AbsoluteUri;
    } catch (Exception ex) {
      _logger.LogError($"GetUrl failed: {filePath}, Error: {ex.Message}");
      throw;
    }
  }

  public async Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default) {
    try {
      var blobClient = _containerClient.GetBlobClient(filePath);
      var result = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
      _logger.LogInformation($"Blob deleted: {filePath}");
      return result.Value;
    } catch (Exception ex) {
      _logger.LogError($"Delete failed: {filePath}, Error: {ex.Message}");
      throw new IOException($"Failed to delete from Azure: {ex.Message}", ex);
    }
  }
}
```

## Dependency Injection Configuration

```csharp
// Program.cs
var storageProvider = builder.Configuration["FileStorage:Provider"] ?? "Local";

if (storageProvider == "Azure") {
  var connectionString = builder.Configuration["AzureStorage:ConnectionString"];
  var containerName = builder.Configuration["AzureStorage:ContainerName"];
  var blobServiceClient = new BlobServiceClient(connectionString);
  var containerClient = blobServiceClient.GetBlobContainerClient(containerName);
  
  builder.Services.AddScoped<IFileStorageService>(_ => new AzureBlobStorageService(containerClient, ...));
} else {
  builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
}
```

## Security Considerations

1. **Path Traversal Prevention**: Never use user-supplied filenames; always use GUID-based paths
2. **Authorization Checks**: IFileStorageService does NOT enforce authorization; DocumentService must check before calling
3. **Error Messages**: Avoid exposing file paths in error messages to end users
4. **Logging**: Log all uploads/downloads for audit trails (especially important for sensitive documents)
5. **Encryption**: For production (Azure), enable encryption at rest and in transit (HTTPS)

## Performance Characteristics

| Operation | Local Filesystem | Azure Blob Storage |
|-----------|------------------|--------------------|
| Upload 25 MB | <5 seconds | 10-15 seconds (network dependent) |
| Download 25 MB | <3 seconds | 10-15 seconds (network dependent) |
| Delete | <1 second | <1 second |
| Get URL | <1ms | <100ms |

Migration from local to Azure has zero impact on DocumentService or UI layer - only configuration changes needed.
