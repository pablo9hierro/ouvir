using Jubilados.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jubilados.Infrastructure.Data.Configurations;

public class NotaItemConfiguration : IEntityTypeConfiguration<NotaItem>
{
    public void Configure(EntityTypeBuilder<NotaItem> builder)
    {
        builder.ToTable("nota_itens");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(i => i.NotaFiscalId).HasColumnName("nota_fiscal_id").IsRequired();
        builder.Property(i => i.ProdutoId).HasColumnName("produto_id").IsRequired();
        builder.Property(i => i.NumeroItem).HasColumnName("numero_item").IsRequired();
        builder.Property(i => i.Quantidade).HasColumnName("quantidade").HasPrecision(15, 4).IsRequired();
        builder.Property(i => i.Unidade).HasColumnName("unidade").HasMaxLength(6);
        builder.Property(i => i.ValorUnitario).HasColumnName("valor_unitario").HasPrecision(21, 10).IsRequired();
        builder.Property(i => i.ValorDesconto).HasColumnName("valor_desconto").HasPrecision(18, 2);
        builder.Property(i => i.ValorTotal).HasColumnName("valor_total").HasPrecision(18, 2).IsRequired();
        builder.Property(i => i.BaseICMS).HasColumnName("base_icms").HasPrecision(18, 2);
        builder.Property(i => i.AliquotaICMS).HasColumnName("aliquota_icms").HasPrecision(5, 2);
        builder.Property(i => i.ValorICMS).HasColumnName("valor_icms").HasPrecision(18, 2);
        builder.Property(i => i.AliquotaIPI).HasColumnName("aliquota_ipi").HasPrecision(5, 2);
        builder.Property(i => i.ValorIPI).HasColumnName("valor_ipi").HasPrecision(18, 2);
        builder.Property(i => i.AliquotaPIS).HasColumnName("aliquota_pis").HasPrecision(5, 2);
        builder.Property(i => i.ValorPIS).HasColumnName("valor_pis").HasPrecision(18, 2);
        builder.Property(i => i.AliquotaCOFINS).HasColumnName("aliquota_cofins").HasPrecision(5, 2);
        builder.Property(i => i.ValorCOFINS).HasColumnName("valor_cofins").HasPrecision(18, 2);

        builder.HasOne(i => i.NotaFiscal)
               .WithMany(n => n.Itens)
               .HasForeignKey(i => i.NotaFiscalId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Produto)
               .WithMany(p => p.NotaItens)
               .HasForeignKey(i => i.ProdutoId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
