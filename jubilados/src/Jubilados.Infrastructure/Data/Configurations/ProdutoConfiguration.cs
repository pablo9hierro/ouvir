using Jubilados.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jubilados.Infrastructure.Data.Configurations;

public class ProdutoConfiguration : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> builder)
    {
        builder.ToTable("produtos");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(p => p.Nome).HasColumnName("nome").HasMaxLength(120).IsRequired();
        builder.Property(p => p.Descricao).HasColumnName("descricao").HasMaxLength(500);
        builder.Property(p => p.NCM).HasColumnName("ncm").HasMaxLength(8).IsRequired();
        builder.Property(p => p.CFOP).HasColumnName("cfop").HasMaxLength(4).IsRequired();
        builder.Property(p => p.CST).HasColumnName("cst").HasMaxLength(3).IsRequired();
        builder.Property(p => p.CSOSN).HasColumnName("csosn").HasMaxLength(3);
        builder.Property(p => p.CEST).HasColumnName("cest").HasMaxLength(7);
        builder.Property(p => p.Unidade).HasColumnName("unidade").HasMaxLength(6);
        builder.Property(p => p.Preco).HasColumnName("preco").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.AliquotaICMS).HasColumnName("aliquota_icms").HasPrecision(5, 2);
        builder.Property(p => p.AliquotaIPI).HasColumnName("aliquota_ipi").HasPrecision(5, 2);
        builder.Property(p => p.AliquotaPIS).HasColumnName("aliquota_pis").HasPrecision(5, 2);
        builder.Property(p => p.AliquotaCOFINS).HasColumnName("aliquota_cofins").HasPrecision(5, 2);
        builder.Property(p => p.EAN).HasColumnName("ean").HasMaxLength(14);
        builder.Property(p => p.QuantidadeEstoque).HasColumnName("quantidade_estoque").HasPrecision(15, 4).HasDefaultValue(0m);
        builder.Property(p => p.Ativo).HasColumnName("ativo").HasDefaultValue(true);
        builder.Property(p => p.CriadoEm).HasColumnName("criado_em").HasDefaultValueSql("NOW()");
        builder.Property(p => p.AtualizadoEm).HasColumnName("atualizado_em").HasDefaultValueSql("NOW()");

        builder.HasOne(p => p.Empresa)
               .WithMany(e => e.Produtos)
               .HasForeignKey(p => p.EmpresaId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
