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

        // Dados fiscais (migration 005)
        builder.Property(e => e.Ramo).HasColumnName("ramo").HasMaxLength(50);
        builder.Property(e => e.RegimeTributario).HasColumnName("regime_tributario").HasMaxLength(30);
        builder.Property(e => e.CRT).HasColumnName("crt").HasDefaultValue(1);
        builder.Property(e => e.CNAE).HasColumnName("cnae").HasMaxLength(7);
        builder.Property(e => e.InscricaoMunicipal).HasColumnName("inscricao_municipal").HasMaxLength(30);
        builder.Property(e => e.EmiteNfse).HasColumnName("emite_nfse").HasDefaultValue(false);
        builder.Property(e => e.InscritoSuframa).HasColumnName("inscrito_suframa").HasDefaultValue(false);
        builder.Property(e => e.CsosnPadrao).HasColumnName("csosn_padrao").HasMaxLength(3).HasDefaultValue("102");
        builder.Property(e => e.CstIcmsPadrao).HasColumnName("cst_icms_padrao").HasMaxLength(3);
        builder.Property(e => e.CstPisPadrao).HasColumnName("cst_pis_padrao").HasMaxLength(2);
        builder.Property(e => e.CstCofinsPadrao).HasColumnName("cst_cofins_padrao").HasMaxLength(2);
        builder.Property(e => e.AliquotaPis).HasColumnName("aliquota_pis").HasPrecision(5, 2).HasDefaultValue(0.65m);
        builder.Property(e => e.AliquotaCofins).HasColumnName("aliquota_cofins").HasPrecision(5, 2).HasDefaultValue(3.00m);
        builder.Property(e => e.AliquotaIss).HasColumnName("aliquota_iss").HasPrecision(5, 2).HasDefaultValue(5.00m);
        builder.Property(e => e.OperaComoSubstitutoTributario).HasColumnName("opera_como_substituto_tributario").HasDefaultValue(false);
        builder.Property(e => e.MvaPadrao).HasColumnName("mva_padrao").HasPrecision(5, 2);
        builder.Property(e => e.PossuiStBebidas).HasColumnName("possui_st_bebidas").HasDefaultValue(false);
        builder.Property(e => e.ContadorNome).HasColumnName("contador_nome").HasMaxLength(100);
        builder.Property(e => e.ContadorCrc).HasColumnName("contador_crc").HasMaxLength(20);
        builder.Property(e => e.ContadorEmail).HasColumnName("contador_email").HasMaxLength(100);
        builder.Property(e => e.FaixaSimples).HasColumnName("faixa_simples").HasMaxLength(10);

        builder.Property(e => e.CriadoEm).HasColumnName("criado_em").HasDefaultValueSql("NOW()");
        builder.Property(e => e.AtualizadoEm).HasColumnName("atualizado_em").HasDefaultValueSql("NOW()");

        builder.HasIndex(e => e.CNPJ).IsUnique();
    }
}
