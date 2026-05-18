using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class Document
{
    [Key]
    public int DocumentId { get; set; }

    [Required]
    public int UserId { get; set; }

    public int? ProjectId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Tags { get; set; }

    [Required]
    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    [Required]
    public long FileSize { get; set; }

    [Required]
    [MaxLength(255)]
    public string MimeType { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string CreatedBy { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string ScanStatus { get; set; } = "Approved";

    // Navigation properties
    [ForeignKey("UserId")]
    public virtual User Uploader { get; set; } = null!;

    [ForeignKey("ProjectId")]
    public virtual Project? Project { get; set; }

    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<DocumentActivityLog> ActivityLogs { get; set; } = new List<DocumentActivityLog>();
}
