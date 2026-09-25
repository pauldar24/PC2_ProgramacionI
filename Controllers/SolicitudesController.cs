using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PC2.Data;
using PC2.Models;
using RabbitMQ.Client;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace PC2.Controllers;

[Authorize]
public class SolicitudesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SolicitudesController> _logger;

    public SolicitudesController(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<SolicitudesController> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    private string? UsuarioActualId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var viewModel = new CrearSolicitudViewModel
        {
            IngresosMensuales = await ObtenerIngresosMensualesAsync()
        };

        return View(viewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CrearSolicitudViewModel model)
    {
        var cliente = await _context.Clientes.FirstOrDefaultAsync(c => c.UsuarioId == UsuarioActualId);

        if (cliente is null)
        {
            cliente = new Cliente
            {
                UsuarioId = UsuarioActualId!,
                IngresosMensuales = 3000m,
                Activo = true
            };

            _context.Clientes.Add(cliente);
            await _context.SaveChangesAsync();
        }

        model.IngresosMensuales = cliente.IngresosMensuales;

        if (!cliente.Activo)
        {
            ModelState.AddModelError(string.Empty, "Su cuenta de cliente está inactiva y no puede registrar solicitudes.");
        }

        if (await _context.SolicitudesCredito.AnyAsync(s => s.ClienteId == cliente.Id && s.Estado == EstadoSolicitud.Pendiente))
        {
            ModelState.AddModelError(string.Empty, "Ya tiene una solicitud en estado Pendiente. Solo puede tener una solicitud activa a la vez.");
        }

        if (model.MontoSolicitado > cliente.IngresosMensuales * 10)
        {
            ModelState.AddModelError(nameof(model.MontoSolicitado),
                $"El monto solicitado no puede superar 10 veces sus ingresos mensuales ({cliente.IngresosMensuales * 10:C}).");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var solicitud = new SolicitudCredito
        {
            ClienteId = cliente.Id,
            MontoSolicitado = model.MontoSolicitado,
            FechaSolicitud = DateTime.UtcNow,
            Estado = EstadoSolicitud.Pendiente
        };

        _context.SolicitudesCredito.Add(solicitud);
        await _context.SaveChangesAsync();

        await PublicarNotificacionRabbitAsync(solicitud);

        TempData["MensajeExito"] = "Su solicitud de crédito fue registrada correctamente y quedó en estado Pendiente.";
        return RedirectToAction(nameof(MisSolicitudes));
    }

    private async Task PublicarNotificacionRabbitAsync(SolicitudCredito solicitud)
    {
        var uri = _configuration["RabbitMQ:Uri"];
        if (string.IsNullOrWhiteSpace(uri))
        {
            _logger.LogWarning("Configuración de RabbitMQ incompleta; no se publicó el mensaje.");
            return;
        }

        try
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri(uri)
            };

            await using var connection = await factory.CreateConnectionAsync();
            await using var channel = await connection.CreateChannelAsync();

            await channel.QueueDeclareAsync(
                queue: "cola_notificaciones",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            var payload = new
            {
                id = solicitud.Id,
                montoSolicitado = solicitud.MontoSolicitado,
                mensaje = "Nueva solicitud registrada"
            };

            var body = JsonSerializer.SerializeToUtf8Bytes(payload);

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: "cola_notificaciones",
                mandatory: false,
                body: body);

            _logger.LogInformation("Mensaje publicado en la cola cola_notificaciones para la solicitud {SolicitudId}.", solicitud.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al publicar la solicitud {SolicitudId} en RabbitMQ.", solicitud.Id);
        }
    }

    private async Task<decimal?> ObtenerIngresosMensualesAsync()
    {
        var cliente = await _context.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UsuarioId == UsuarioActualId);

        return cliente?.IngresosMensuales;
    }

    [HttpGet]
    public async Task<IActionResult> MisSolicitudes(
        EstadoSolicitud? estado,
        decimal? montoMin,
        decimal? montoMax,
        DateTime? fechaInicio,
        DateTime? fechaFin)
    {
        var viewModel = new MisSolicitudesViewModel
        {
            Estado = estado,
            MontoMin = montoMin,
            MontoMax = montoMax,
            FechaInicio = fechaInicio,
            FechaFin = fechaFin
        };

        if (montoMin < 0)
        {
            ModelState.AddModelError(nameof(viewModel.MontoMin), "El monto mínimo no puede ser menor a cero.");
        }

        if (montoMax < 0)
        {
            ModelState.AddModelError(nameof(viewModel.MontoMax), "El monto máximo no puede ser menor a cero.");
        }

        if (montoMin.HasValue && montoMax.HasValue && montoMin > montoMax)
        {
            ModelState.AddModelError(nameof(viewModel.MontoMax), "El monto máximo no puede ser menor que el monto mínimo.");
        }

        if (fechaInicio.HasValue && fechaFin.HasValue && fechaInicio > fechaFin)
        {
            ModelState.AddModelError(nameof(viewModel.FechaFin), "La fecha de inicio no puede ser posterior a la fecha de fin.");
        }

        if (!ModelState.IsValid)
        {
            viewModel.Solicitudes = new List<SolicitudCredito>();
            return View(viewModel);
        }

        var cliente = await _context.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UsuarioId == UsuarioActualId);

        if (cliente is null)
        {
            return View(viewModel);
        }

        viewModel.ClienteId = cliente.Id;

        var query = _context.SolicitudesCredito.AsNoTracking()
            .Include(s => s.Cliente)
            .Where(s => s.ClienteId == cliente.Id);

        if (estado.HasValue)
        {
            query = query.Where(s => s.Estado == estado.Value);
        }

        if (montoMin.HasValue)
        {
            query = query.Where(s => s.MontoSolicitado >= montoMin.Value);
        }

        if (montoMax.HasValue)
        {
            query = query.Where(s => s.MontoSolicitado <= montoMax.Value);
        }

        if (fechaInicio.HasValue)
        {
            query = query.Where(s => s.FechaSolicitud >= fechaInicio.Value);
        }

        if (fechaFin.HasValue)
        {
            query = query.Where(s => s.FechaSolicitud <= fechaFin.Value);
        }

        viewModel.Solicitudes = await query
            .OrderByDescending(s => s.FechaSolicitud)
            .ToListAsync();

        return View(viewModel);
    }

    [HttpGet]
    public async Task<IActionResult> Detalle(int id)
    {
        var cliente = await _context.Clientes.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UsuarioId == UsuarioActualId);

        if (cliente is null)
        {
            return NotFound();
        }

        var solicitud = await _context.SolicitudesCredito.AsNoTracking()
            .Include(s => s.Cliente)
            .FirstOrDefaultAsync(s => s.Id == id && s.ClienteId == cliente.Id);

        if (solicitud is null)
        {
            return NotFound();
        }

        return View(solicitud);
    }
}