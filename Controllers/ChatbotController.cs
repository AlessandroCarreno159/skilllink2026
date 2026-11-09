using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SkillLink_dotnet.Data;
using SkillLink_dotnet.Models;
using SkillLink_dotnet.Services.Chatbot;

namespace SkillLink_dotnet.Controllers;

[Authorize]
[Route("Chatbot")]
public class ChatbotController(
    ApplicationDbContext db,
    UserManager<ApplicationUser> users,
    ChatbotOrchestrator orchestrator,
    ChatSessionStore store,
    IOptions<ChatbotOptions> options) : Controller
{
    [HttpPost("Message")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Message([FromBody] ChatMessageRequest request, CancellationToken ct)
    {
        var usuario = await users.GetUserAsync(User);
        if (usuario is null) return Unauthorized();

        var contexto = new ChatUserContext(
            usuario.Id,
            usuario.Rol,
            ChatCardFactory.NombrePublicoSeguro(usuario));

        try
        {
            var resultado = await orchestrator.SendAsync(contexto, request.Message, request.ConversationId, ct);
            return Ok(new
            {
                ok = true,
                conversationId = resultado.ConversationId,
                messageId = resultado.MessageId,
                reply = resultado.Reply,
                cards = resultado.Cards,
                suggestions = resultado.Suggestions,
            });
        }
        catch (ChatbotException ex)
        {
            if (ex.RetryAfterSeconds > 0)
                Response.Headers["Retry-After"] = ex.RetryAfterSeconds.ToString();

            return StatusCode(ex.StatusCode, new
            {
                ok = false,
                error = ex.Message,
                retryAfter = ex.RetryAfterSeconds,
            });
        }
    }

    [HttpGet("History")]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        var usuario = await users.GetUserAsync(User);
        if (usuario is null) return Unauthorized();

        var sesion = await store.GetAsync(usuario.Id, ct);
        return Ok(new
        {
            ok = true,
            conversationId = sesion.ConversationId,
            messages = sesion.Messages.Select(m => new
            {
                role = m.Role,
                text = m.Text,
                at = m.At,
            }),
        });
    }

    [HttpPost("Feedback")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Feedback([FromBody] ChatFeedbackRequest request, CancellationToken ct)
    {
        var usuario = await users.GetUserAsync(User);
        if (usuario is null) return Unauthorized();

        var conversationId = request.ConversationId?.Trim();
        var messageId = request.MessageId?.Trim();
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(messageId))
            return BadRequest(new { ok = false, error = "Faltan datos del mensaje." });
        if (conversationId.Length > 64 || messageId.Length > 64)
            return BadRequest(new { ok = false, error = "Los identificadores del mensaje no son válidos." });
        if (request.Rating is < 1 or > 5)
            return BadRequest(new { ok = false, error = "La calificación debe estar entre 1 y 5." });

        var comentario = request.Comment?.Trim();
        if (comentario is { Length: > 500 })
            return BadRequest(new { ok = false, error = "El comentario no puede superar los 500 caracteres." });

        var existe = await db.ChatbotFeedbacks.AnyAsync(f =>
            f.UsuarioId == usuario.Id && f.MessageId == messageId, ct);
        if (!existe)
        {
            db.ChatbotFeedbacks.Add(new ChatbotFeedback
            {
                UsuarioId = usuario.Id,
                ConversationId = conversationId,
                MessageId = messageId,
                Rating = request.Rating,
                Comment = string.IsNullOrWhiteSpace(comentario) ? null : comentario,
            });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Otro envío simultáneo ya registró el mismo feedback.
                db.ChangeTracker.Clear();
            }
        }

        return Ok(new { ok = true });
    }

    [HttpPost("Clear")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        var usuario = await users.GetUserAsync(User);
        if (usuario is null) return Unauthorized();

        await store.ClearAsync(usuario.Id, ct);
        return Ok(new { ok = true });
    }

    [HttpGet("Status")]
    public IActionResult Status() => Ok(new
    {
        ok = true,
        enabled = options.Value.IsConfigured,
        supportUrl = options.Value.SupportUrl,
    });
}
