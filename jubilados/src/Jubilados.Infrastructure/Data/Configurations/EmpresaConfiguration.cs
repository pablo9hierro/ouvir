using Jubilados.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jubilados.Infrastructure.Data.Configurations;

public class EmpresaConfiguration : IEntityTypeConfiguration<Empresa>
{
    public void Configure(EntityTypeBuilder<Empresa> builder)
    {
        builder.ToTable("empresas");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.CNPJ).HasColumnName("cnpj").HasMaxLength(18).IsRequired();
        builder.Property(e => e.RazaoSocial).HasColumnName("razao_social").HasMaxLength(150).IsRequired();
        builder.Property(e => e.NomeFantasia).HasColumnName("nome_fantasia").HasMaxLength(100);
        builder.Property(e => e.InscricaoEstadual).HasColumnName("inscricao_estadual").HasMaxLength(20);
        builder.Property(e => e.Logradouro).HasColumnName("logradouro").HasMaxLength(100);
        builder.Property(e => e.Numero).HasColumnName("numero").HasMaxLength(10);
        builder.Property(e => e.Complemento).HasColumnName("complemento").HasMaxLength(60);
        builder.Property(e => e.Bairro).HasColumnName("bairro").HasMaxLength(60);
        builder.Property(e => e.Municipio).HasColumnName("municipio").HasMaxLength(60);
        builder.Property(e => e.UF).HasColumnName("uf").HasMaxLength(2);
        builder.Property(e => e.CEP).HasColumnName("cep").HasMaxLength(9);
        builder.Property(e => e.Telefone).HasColumnName("telefone").HasMaxLength(15);
        builder.Property(e => e.Email).HasColumnName("email").HasMaxLength(100);
        builder.Property(e => e.CertificadoBase64).HasColumnName("certificado_base64").HasColumnType("text");
        builder.Property(e => e.CertificadoSenha).HasColumnName("certificado_senha").HasMaxLength(256);
        builder.Property(e => e.CertificadoValidade).HasColumnName("certificado_validade");
        builder.Property(e => e.NfceCscId).HasColumnName("nfce_csc_id").HasMaxLength(10);
        builder.Property(e => e.NfceCscToken).HasColumnName("nfce_csc_token").HasMaxLength(64);
        builder.Property(e => e.CriadoEm).HasColumnName("criado_em").HasDefaultValueSql("NOW()");
        builder.Property(e => e.AtualizadoEm).HasColumnName("atualizado_em").HasDefaultValueSql("NOW()");

        builder.HasIndex(e => e.CNPJ).IsUnique();
    }
}
