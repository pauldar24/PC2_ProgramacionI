using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PC2.Data;
using PC2.Models;

namespace PC2.Controllers;

[Authorize(Roles = "Analista")]
public class AnalistaController : Controller
{
    private readonly ApplicationDbContext _context;

    public AnalistaController(ApplicationDbContext context)
    {
        _context = context;
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

        TempData["MensajeExito"] = estado == EstadoSolicitud.Aprobado
            ? $"La solicitud #{id} fue aprobada correctamente."
            : $"La solicitud #{id} fue rechazada.";

        return RedirectToAction(nameof(Index));
    }
}