using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PC2.Models;

namespace PC2.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<SolicitudCredito> SolicitudesCredito => Set<SolicitudCredito>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Cliente>(cliente =>
        {
            cliente.ToTable("Clientes");
            cliente.Property(c => c.UsuarioId).IsRequired();
            cliente.Property(c => c.IngresosMensuales).HasPrecision(18, 2);
            cliente.ToTable(t => t.HasCheckConstraint("CK_Clientes_IngresosMensuales_Positivos", "IngresosMensuales > 0"));
        });

        builder.Entity<SolicitudCredito>(solicitud =>
        {
            solicitud.ToTable("SolicitudesCredito");
            solicitud.Property(s => s.MontoSolicitado).HasPrecision(18, 2);
            solicitud.Property(s => s.MotivoRechazo).HasMaxLength(500);
            solicitud.ToTable(t => t.HasCheckConstraint("CK_Solicitudes_Monto_Positivo", "MontoSolicitado > 0"));

            solicitud.HasOne(s => s.Cliente)
                .WithMany(c => c.Solicitudes)
                .HasForeignKey(s => s.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}