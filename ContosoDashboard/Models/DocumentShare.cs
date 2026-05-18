using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentShare
{
    [Key]
    public int ShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    [Required]
    public int SharedWithUserId { get; set; }

    [Required]
    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    [Required]
    [MaxLength(255)]
    public string SharedBy { get; set; } = string.Empty;

    // Navigation properties
    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("SharedWithUserId")]
    public virtual User SharedWithUser { get; set; } = null!;
}
