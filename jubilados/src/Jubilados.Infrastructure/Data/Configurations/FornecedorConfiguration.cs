using Jubilados.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Jubilados.Infrastructure.Data.Configurations;

public class FornecedorConfiguration : IEntityTypeConfiguration<Fornecedor>
{
    public void Configure(EntityTypeBuilder<Fornecedor> builder)
    {
        builder.ToTable("fornecedores");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.EmpresaId).HasColumnName("empresa_id").IsRequired();
        builder.Property(f => f.Nome).HasColumnName("nome").HasMaxLength(150).IsRequired();
        builder.Property(f => f.CPF_CNPJ).HasColumnName("cpf_cnpj").HasMaxLength(18).IsRequired();
        builder.Property(f => f.InscricaoEstadual).HasColumnName("inscricao_estadual").HasMaxLength(20);
        builder.Property(f => f.Email).HasColumnName("email").HasMaxLength(100);
        builder.Property(f => f.Telefone).HasColumnName("telefone").HasMaxLength(15);
        builder.Property(f => f.Logradouro).HasColumnName("logradouro").HasMaxLength(100);
        builder.Property(f => f.Numero).HasColumnName("numero").HasMaxLength(10);
        builder.Property(f => f.Complemento).HasColumnName("complemento").HasMaxLength(60);
        builder.Property(f => f.Bairro).HasColumnName("bairro").HasMaxLength(60);
        builder.Property(f => f.Municipio).HasColumnName("municipio").HasMaxLength(60);
        builder.Property(f => f.CodigoMunicipio).HasColumnName("codigo_municipio").HasMaxLength(7);
        builder.Property(f => f.UF).HasColumnName("uf").HasMaxLength(2);
        builder.Property(f => f.CEP).HasColumnName("cep").HasMaxLength(9);
        builder.Property(f => f.Pais).HasColumnName("pais").HasMaxLength(60);
        builder.Property(f => f.CodigoPais).HasColumnName("codigo_pais").HasMaxLength(4);
        builder.Property(f => f.CriadoEm).HasColumnName("criado_em").HasDefaultValueSql("NOW()");
        builder.Property(f => f.AtualizadoEm).HasColumnName("atualizado_em").HasDefaultValueSql("NOW()");

        builder.HasOne(f => f.Empresa)
               .WithMany(e => e.Fornecedores)
               .HasForeignKey(f => f.EmpresaId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}