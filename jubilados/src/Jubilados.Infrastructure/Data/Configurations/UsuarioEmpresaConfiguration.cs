using Jubilados.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jubilados.Infrastructure.Data.Configurations;

public class UsuarioEmpresaConfiguration : IEntityTypeConfiguration<UsuarioEmpresa>
{
    public void Configure(EntityTypeBuilder<UsuarioEmpresa> builder)
    {
        builder.ToTable("usuario_empresa");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(u => u.SupabaseUserId).HasColumnName("supabase_user_id").IsRequired();
        builder.Property(u => u.EmpresaId).HasColumnName("empresa_id");
        builder.Property(u => u.OnboardingConcluido).HasColumnName("onboarding_concluido").HasDefaultValue(false);
        builder.Property(u => u.CriadoEm).HasColumnName("criado_em").HasDefaultValueSql("NOW()");

        builder.HasIndex(u => u.SupabaseUserId).IsUnique();

        builder.HasOne(u => u.Empresa)
               .WithMany()
               .HasForeignKey(u => u.EmpresaId)
               .OnDelete(DeleteBehavior.SetNull);
    }
}
