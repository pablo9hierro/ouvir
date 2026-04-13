namespace Jubilados.Application.Configuration;

/// <summary>
/// Configurações do ambiente NFe lidas do appsettings.json seção "NFe".
/// </summary>
public class NFeOptions
{
    public const string Section = "NFe";

    /// <summary>1 = Produção, 2 = Homologação</summary>
    public string Ambiente { get; set; } = "2";

    /// <summary>Código da UF do emitente (ex: 25 = PB, 35 = SP)</summary>
    public string CodigoUF { get; set; } = "25";

    /// <summary>Código IBGE do município do emitente (ex: 2507507 = João Pessoa PB)</summary>
    public string CodigoMunicipio { get; set; } = "2507507";

    /// <summary>Modelo do documento: 55 = NFe, 65 = NFCe</summary>
    public string ModeloNFe { get; set; } = "55";

    /// <summary>Versão do schema NFe</summary>
    public string VersaoNFe { get; set; } = "4.00";

    /// <summary>Caminho local dos schemas XSD (relativo ao binário)</summary>
    public string SchemaPath { get; set; } = "schemas";

    /// <summary>URL do WS SEFAZ para homologação — Autorização (SVRS)</summary>
    public string SefazUrlHomologacao { get; set; } = "https://nfe-homologacao.sefazrs.rs.gov.br/ws/NfeAutorizacao/NfeAutorizacao4.asmx";

    /// <summary>URL do WS SEFAZ para produção — Autorização (SVRS)</summary>
    public string SefazUrlProducao { get; set; } = "https://nfe.svrs.rs.gov.br/ws/NfeAutorizacao/NfeAutorizacao4.asmx";

    /// <summary>URL do WS SEFAZ para homologação — Consulta Protocolo (SVRS)</summary>
    public string SefazUrlConsultaHom { get; set; } = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";

    /// <summary>URL do WS SEFAZ para produção — Consulta Protocolo (SVRS)</summary>
    public string SefazUrlConsultaProd { get; set; } = "https://nfe.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";

    /// <summary>URL do WS SEFAZ para homologação — Status Serviço (SVRS)</summary>
    public string SefazUrlStatusHom { get; set; } = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";

    /// <summary>URL do WS SEFAZ para produção — Status Serviço (SVRS)</summary>
    public string SefazUrlStatusProd { get; set; } = "https://nfe.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";

    /// <summary>URL do WS SEFAZ para homologação — Inutilização (SVRS)</summary>
    public string SefazUrlInutilizacaoHom { get; set; } = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeInutilizacao/NfeInutilizacao4.asmx";

    /// <summary>URL do WS SEFAZ para produção — Inutilização (SVRS)</summary>
    public string SefazUrlInutilizacaoProd { get; set; } = "https://nfe.svrs.rs.gov.br/ws/NfeInutilizacao/NfeInutilizacao4.asmx";

    /// <summary>URL do WS SEFAZ para homologação — Recepção de Eventos (CCe, Cancelamento, Manifestação)</summary>
    public string SefazUrlEventoHom { get; set; } = "https://nfe-homologacao.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";

    /// <summary>URL do WS SEFAZ para produção — Recepção de Eventos</summary>
    public string SefazUrlEventoProd { get; set; } = "https://nfe.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";

    // ── Contingência SVC-AN (SEFAZ Virtual do Ambiente Nacional) ─────────────
    /// <summary>URL SVC-AN homologação — fallback quando SVRS está inoperante</summary>
    public string UrlSvcAnHom { get; set; } = "https://hom.nfe.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";

    /// <summary>URL SVC-AN produção</summary>
    public string UrlSvcAnProd { get; set; } = "https://www.nfe.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx";

    // ── NFC-e (Cupom Fiscal Eletrônico — mod=65) ──────────────────────────────
    /// <summary>URL WS SVRS NFC-e homologação — Autorização</summary>
    public string UrlNfceAutorizacaoHom { get; set; } = "https://nfce-homologacao.svrs.rs.gov.br/ws/NfceAutorizacao/NFCeAutorizacao4.asmx";

    /// <summary>URL WS SVRS NFC-e produção — Autorização</summary>
    public string UrlNfceAutorizacaoProd { get; set; } = "https://nfce.svrs.rs.gov.br/ws/NfceAutorizacao/NFCeAutorizacao4.asmx";

    /// <summary>URL base para consulta QR Code NFC-e (homologação)</summary>
    public string UrlNfceQrCodeHom { get; set; } = "https://nfce-homologacao.svrs.rs.gov.br/consulta.aspx";

    /// <summary>URL base para consulta QR Code NFC-e (produção)</summary>
    public string UrlNfceQrCodeProd { get; set; } = "https://nfce.svrs.rs.gov.br/consulta.aspx";

    public bool IsHomologacao => Ambiente == "2";

    public string UrlConsulta       => IsHomologacao ? SefazUrlConsultaHom       : SefazUrlConsultaProd;
    public string UrlStatus         => IsHomologacao ? SefazUrlStatusHom         : SefazUrlStatusProd;
    public string UrlInutilizacao   => IsHomologacao ? SefazUrlInutilizacaoHom   : SefazUrlInutilizacaoProd;
    public string UrlEvento         => IsHomologacao ? SefazUrlEventoHom         : SefazUrlEventoProd;
    public string UrlSvcAn          => IsHomologacao ? UrlSvcAnHom               : UrlSvcAnProd;
    public string UrlNfceAutorizacao => IsHomologacao ? UrlNfceAutorizacaoHom    : UrlNfceAutorizacaoProd;
    public string UrlNfceQrCode     => IsHomologacao ? UrlNfceQrCodeHom          : UrlNfceQrCodeProd;
}
