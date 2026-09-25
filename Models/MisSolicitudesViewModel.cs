namespace PC2.Models;

public class MisSolicitudesViewModel
{
    public EstadoSolicitud? Estado { get; set; }
    public int? ClienteId { get; set; }
    public decimal? MontoMin { get; set; }
    public decimal? MontoMax { get; set; }
    public DateTime? FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public List<SolicitudCredito> Solicitudes { get; set; } = new();
}