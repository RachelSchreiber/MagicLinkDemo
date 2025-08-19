using System.ComponentModel.DataAnnotations;

namespace MagicLinkDemo.Models;

public class MagicLinkRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}
