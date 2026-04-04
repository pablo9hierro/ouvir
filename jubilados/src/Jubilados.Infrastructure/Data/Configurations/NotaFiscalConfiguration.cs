using Jubilados.Domain.Entities;
using Jubilados.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jubilados.Infrastructure.Data.Configurations;

public class NotaFiscalConfiguration : IEntityTypeConfiguration<NotaFiscal>
{
    public void Configure(EntityTypeBuilder<NotaFiscal> builder)
    {
        builder.ToTable("notas_fiscais");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(n => n.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(n => n.ClienteId).HasColumnName("cliente_id").IsRequired();
        builder.Property(n => n.Numero).HasColumnName("numero").IsRequired();
        builder.Property(n => n.Serie).HasColumnName("serie").HasMaxLength(3).IsRequired();
        builder.Property(n => n.ChaveAcesso).HasColumnName("chave_acesso").HasMaxLength(44);
        builder.Property(n => n.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(n => n.TipoOperacao).HasColumnName("tipo_operacao").HasMaxLength(1);
        builder.Property(n => n.NaturezaOperacao).HasColumnName("natureza_operacao").HasMaxLength(60);
        builder.Property(n => n.ValorProdutos).HasColumnName("valor_produtos").HasPrecision(18, 2);
        builder.Property(n => n.ValorDesconto).HasColumnName("valor_desconto").HasPrecision(18, 2);
        builder.Property(n => n.ValorFrete).HasColumnName("valor_frete").HasPrecision(18, 2);
        builder.Property(n => n.ValorSeguro).HasColumnName("valor_seguro").HasPrecision(18, 2);
        builder.Property(n => n.ValorOutros).HasColumnName("valor_outros").HasPrecision(18, 2);
        builder.Property(n => n.ValorICMS).HasColumnName("valor_icms").HasPrecision(18, 2);
        builder.Property(n => n.ValorIPI).HasColumnName("valor_ipi").HasPrecision(18, 2);
        builder.Property(n => n.ValorPIS).HasColumnName("valor_pis").HasPrecision(18, 2);
        builder.Property(n => n.ValorCOFINS).HasColumnName("valor_cofins").HasPrecision(18, 2);
        builder.Property(n => n.ValorTotal).HasColumnName("valor_total").HasPrecision(18, 2);
        builder.Property(n => n.XmlEnvio).HasColumnName("xml_envio").HasColumnType("text");
        builder.Property(n => n.XmlRetorno).HasColumnName("xml_retorno").HasColumnType("text");
        builder.Property(n => n.Protocolo).HasColumnName("protocolo").HasMaxLength(20);
        builder.Property(n => n.CStat).HasColumnName("cstat").HasMaxLength(3);
        builder.Property(n => n.XMotivo).HasColumnName("xmotivo").HasMaxLength(255);
        builder.Property(n => n.NSU).HasColumnName("nsu");
        builder.Property(n => n.Manifestada).HasColumnName("manifestada").HasDefaultValue(false);
        builder.Property(n => n.TipoManifestacao).HasColumnName("tipo_manifestacao").HasConversion<int?>();
        builder.Property(n => n.EmitidaEm).HasColumnName("emitida_em").HasDefaultValueSql("NOW()");
        builder.Property(n => n.AutorizadaEm).HasColumnName("autorizada_em");
        builder.Property(n => n.CriadoEm).HasColumnName("criado_em").HasDefaultValueSql("NOW()");
        builder.Property(n => n.AtualizadoEm).HasColumnName("atualizado_em").HasDefaultValueSql("NOW()");

        builder.HasOne(n => n.Empresa)
               .WithMany(e => e.NotasFiscais)
               .HasForeignKey(n => n.EmpresaId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(n => n.Cliente)
               .WithMany(c => c.NotasFiscais)
               .HasForeignKey(n => n.ClienteId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(n => n.ChaveAcesso);
        builder.HasIndex(n => new { n.EmpresaId, n.Numero, n.Serie }).IsUnique();
    }
}
