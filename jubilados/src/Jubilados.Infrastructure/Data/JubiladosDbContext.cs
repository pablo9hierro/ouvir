using Jubilados.Domain.Entities;
using Jubilados.Infrastructure.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Jubilados.Infrastructure.Data;

public class JubiladosDbContext : DbContext
{
    public JubiladosDbContext(DbContextOptions<JubiladosDbContext> options) : base(options) { }

    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Produto> Produtos => Set<Produto>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Fornecedor> Fornecedores => Set<Fornecedor>();
    public DbSet<NotaFiscal> NotasFiscais => Set<NotaFiscal>();
    public DbSet<NotaItem> NotaItens => Set<NotaItem>();
    public DbSet<UsuarioEmpresa> UsuarioEmpresas => Set<UsuarioEmpresa>();
    public DbSet<Usuario>        Usuarios        => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new EmpresaConfiguration());
        modelBuilder.ApplyConfiguration(new ProdutoConfiguration());
        modelBuilder.ApplyConfiguration(new ClienteConfiguration());
        modelBuilder.ApplyConfiguration(new FornecedorConfiguration());
        modelBuilder.ApplyConfiguration(new NotaFiscalConfiguration());
        modelBuilder.ApplyConfiguration(new NotaItemConfiguration());
        modelBuilder.ApplyConfiguration(new UsuarioEmpresaConfiguration());
        modelBuilder.ApplyConfiguration(new UsuarioConfiguration());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AtualizarTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void AtualizarTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            if (entry.Properties.Any(p => p.Metadata.Name == "AtualizadoEm"))
                entry.Property("AtualizadoEm").CurrentValue = DateTime.UtcNow;

            if (entry.State == EntityState.Added &&
                entry.Properties.Any(p => p.Metadata.Name == "CriadoEm"))
                entry.Property("CriadoEm").CurrentValue = DateTime.UtcNow;
        }
    }
}
