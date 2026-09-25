using System.ComponentModel.DataAnnotations;

namespace PC2.Models;

public class CrearSolicitudViewModel
{
    [Required(ErrorMessage = "El monto solicitado es obligatorio.")]
    [Range(typeof(decimal), "0.01", "79228162514264337593543950335", ErrorMessage = "El monto solicitado debe ser mayor a cero.")]
    [Display(Name = "Monto Solicitado")]
    public decimal MontoSolicitado { get; set; }

    public decimal? IngresosMensuales { get; set; }

    public decimal? MontoMaximoPermitido => IngresosMensuales.HasValue ? IngresosMensuales * 10 : null;
}