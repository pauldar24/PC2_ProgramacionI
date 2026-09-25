using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PC2.Data;
using PC2.Models;
using System.Security.Claims;

namespace PC2.Controllers;

[Authorize]
public class SolicitudesController : Controller
{
    private readonly ApplicationDbContext _context;

    public SolicitudesController(ApplicationDbContext context)
    {
        _context = context;
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
            ModelState.AddModelError(string.Empty, "No se encontró un cliente asociado a su cuenta.");
        }
        else
        {
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

        TempData["MensajeExito"] = "Su solicitud de crédito fue registrada correctamente y quedó en estado Pendiente.";
        return RedirectToAction(nameof(MisSolicitudes));
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