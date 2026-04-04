using Jubilados.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jubilados.Infrastructure.Data.Configurations;

public class ClienteConfiguration : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> builder)
    {
        builder.ToTable("clientes");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(c => c.Nome).HasColumnName("nome").HasMaxLength(150).IsRequired();
        builder.Property(c => c.CPF_CNPJ).HasColumnName("cpf_cnpj").HasMaxLength(18).IsRequired();
        builder.Property(c => c.InscricaoEstadual).HasColumnName("inscricao_estadual").HasMaxLength(20);
        builder.Property(c => c.Email).HasColumnName("email").HasMaxLength(100);
        builder.Property(c => c.Telefone).HasColumnName("telefone").HasMaxLength(15);
        builder.Property(c => c.Logradouro).HasColumnName("logradouro").HasMaxLength(100);
        builder.Property(c => c.Numero).HasColumnName("numero").HasMaxLength(10);
        builder.Property(c => c.Complemento).HasColumnName("complemento").HasMaxLength(60);
        builder.Property(c => c.Bairro).HasColumnName("bairro").HasMaxLength(60);
        builder.Property(c => c.Municipio).HasColumnName("municipio").HasMaxLength(60);
        builder.Property(c => c.CodigoMunicipio).HasColumnName("codigo_municipio").HasMaxLength(7);
        builder.Property(c => c.UF).HasColumnName("uf").HasMaxLength(2);
        builder.Property(c => c.CEP).HasColumnName("cep").HasMaxLength(9);
        builder.Property(c => c.Pais).HasColumnName("pais").HasMaxLength(60);
        builder.Property(c => c.CodigoPais).HasColumnName("codigo_pais").HasMaxLength(4);
        builder.Property(c => c.CriadoEm).HasColumnName("criado_em").HasDefaultValueSql("NOW()");
        builder.Property(c => c.AtualizadoEm).HasColumnName("atualizado_em").HasDefaultValueSql("NOW()");

        builder.HasOne(c => c.Empresa)
               .WithMany(e => e.Clientes)
               .HasForeignKey(c => c.EmpresaId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}
