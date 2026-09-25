namespace PC2.Models;

public class Cliente
{
    public int Id { get; set; }
    public string UsuarioId { get; set; } = string.Empty;
    public decimal IngresosMensuales { get; set; }
    public bool Activo { get; set; }

    public ICollection<SolicitudCredito> Solicitudes { get; set; } = new List<SolicitudCredito>();
}