using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PC2.Data;
using PC2.Models;
using System.Text;
using System.Text.Json;

namespace PC2.Controllers;

[Authorize(Roles = "Analista")]
public class AnalistaController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AnalistaController> _logger;
    private readonly HttpClient _httpClient;

    public AnalistaController(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<AnalistaController> logger,
        HttpClient httpClient)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _httpClient = httpClient;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var pendientes = await _context.SolicitudesCredito.AsNoTracking()
            .Include(s => s.Cliente)
            .Where(s => s.Estado == EstadoSolicitud.Pendiente)
            .OrderByDescending(s => s.FechaSolicitud)
            .ToListAsync();

        return View(pendientes);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Evaluar(int id, EstadoSolicitud estado, string? motivoRechazo)
    {
        if (estado == EstadoSolicitud.Rechazado && string.IsNullOrWhiteSpace(motivoRechazo))
        {
            TempData["MensajeError"] = "Debe indicar el motivo del rechazo.";
            return RedirectToAction(nameof(Index));
        }

        var solicitud = await _context.SolicitudesCredito.FirstOrDefaultAsync(s => s.Id == id);
        if (solicitud is null)
        {
            TempData["MensajeError"] = "La solicitud indicada no existe.";
            return RedirectToAction(nameof(Index));
        }

        solicitud.Estado = estado;
        solicitud.MotivoRechazo = estado == EstadoSolicitud.Rechazado ? motivoRechazo : null;

        await _context.SaveChangesAsync();

        await PublicarNotificacionAsync(solicitud.ClienteId, estado);

        TempData["MensajeExito"] = estado == EstadoSolicitud.Aprobado
            ? $"La solicitud #{id} fue aprobada correctamente."
            : $"La solicitud #{id} fue rechazada.";

        return RedirectToAction(nameof(Index));
    }

    private async Task PublicarNotificacionAsync(int clienteId, EstadoSolicitud estado)
    {
        var clusterId = _configuration["PieSocket:ClusterId"];
        var apiKey = _configuration["PieSocket:ApiKey"];

        if (string.IsNullOrWhiteSpace(clusterId) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Configuración de PieSocket incompleta; no se publicó la notificación.");
            return;
        }

        var url = $"https://{clusterId}.piesocket.com/api/publish?api_key={apiKey}";
        var payload = new
        {
            channel_id = $"canal-cliente-{clienteId}",
            message = $"Tu solicitud de crédito ha sido {estado.ToString().ToLower()}"
        };

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation(
                "Notificación publicada en canal canal-cliente-{ClienteId} (HTTP {StatusCode}).",
                clienteId,
                (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al publicar la notificación de la evaluación en PieSocket.");
        }
    }
}