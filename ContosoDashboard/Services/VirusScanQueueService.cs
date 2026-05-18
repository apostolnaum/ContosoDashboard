namespace ContosoDashboard.Services;

public class VirusScanMessage
{
    public int DocumentId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
}

public interface IVirusScanQueueService
{
    Task EnqueueAsync(VirusScanMessage message, CancellationToken cancellationToken = default);
}

public class LocalVirusScanQueueService : IVirusScanQueueService
{
    // In local/training environments, virus scanning is a no-op.
    // Documents are set to ScanStatus="Approved" immediately on upload.
    public Task EnqueueAsync(VirusScanMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
