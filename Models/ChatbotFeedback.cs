using System.ComponentModel.DataAnnotations;

namespace SkillLink_dotnet.Models;

public class ChatbotFeedback
{
    public int Id { get; set; }
    [Required] public string UsuarioId { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string ConversationId { get; set; } = string.Empty;
    [Required, MaxLength(64)] public string MessageId { get; set; } = string.Empty;
    [Range(1, 5)] public int Rating { get; set; }
    [MaxLength(500)] public string? Comment { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
