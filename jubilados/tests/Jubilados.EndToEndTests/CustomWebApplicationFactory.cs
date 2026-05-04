using Jubilados.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jubilados.EndToEndTests;

public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "Jubilados.EndToEnd." + Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<JubiladosDbContext>>();
            services.RemoveAll<JubiladosDbContext>();
            services.AddDbContext<JubiladosDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}