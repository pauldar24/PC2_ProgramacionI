using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PC2.Models;

namespace PC2.Data;

public static class DbSeeder
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        await context.Database.MigrateAsync();

        var analistaRole = "Analista";

        if (!await roleManager.RoleExistsAsync(analistaRole))
        {
            await roleManager.CreateAsync(new IdentityRole(analistaRole));
        }

        const string analistaEmail = "analista@creditos.com";
        var analista = await userManager.FindByEmailAsync(analistaEmail);
        if (analista is null)
        {
            analista = new IdentityUser
            {
                UserName = analistaEmail,
                Email = analistaEmail,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(analista, "Analista123!");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(analista, analistaRole);
            }
        }

        if (!await context.Clientes.AnyAsync())
        {
            var cliente1 = new Cliente
            {
                UsuarioId = analista.Id,
                IngresosMensuales = 3500m,
                Activo = true
            };

            var cliente2 = new Cliente
            {
                UsuarioId = "seed-cliente-2",
                IngresosMensuales = 4800m,
                Activo = false
            };

            context.Clientes.AddRange(cliente1, cliente2);
            await context.SaveChangesAsync();

            context.SolicitudesCredito.AddRange(
                new SolicitudCredito
                {
                    ClienteId = cliente1.Id,
                    MontoSolicitado = 12000m,
                    FechaSolicitud = DateTime.UtcNow,
                    Estado = EstadoSolicitud.Pendiente
                },
                new SolicitudCredito
                {
                    ClienteId = cliente2.Id,
                    MontoSolicitado = 25000m,
                    FechaSolicitud = DateTime.UtcNow.AddDays(-7),
                    Estado = EstadoSolicitud.Aprobado
                });

            await context.SaveChangesAsync();
        }
        else
        {
            var seedCliente1 = await context.Clientes.FirstOrDefaultAsync(c => c.UsuarioId == "seed-cliente-1");
            if (seedCliente1 is not null)
            {
                seedCliente1.UsuarioId = analista.Id;
                await context.SaveChangesAsync();
            }
        }
    }
}