namespace Jubilados.Infrastructure.Services;

/// <summary>Config de webservice fiscal por UF -- autorização/consulta/status/
/// inutilização/evento SVRS são a mesma URL pra todo estado do grupo SVRS
/// (só troca cUF no XML); a URL de consulta do QR Code da NFC-e, porém, é
/// SEMPRE específica de cada estado (site oficial daquela SEFAZ) -- usar a
/// URL errada aqui derruba a emissão com cStat 395 "Endereço do site da UF
/// da consulta via QR-Code diverge do previsto" (achado testando emissão
/// real em homologação pra PB).
///
/// Populado só com UFs CONFIRMADAS contra fonte oficial -- nunca adicionar
/// uma UF aqui por suposição/blog de terceiros: uma URL errada aqui é
/// silenciosa até alguém tentar emitir de verdade naquele estado.</summary>
public sealed record UfWebserviceInfo(
    string CodigoUF,
    string UrlAutorizacaoHom, string UrlAutorizacaoProd,
    string UrlConsultaHom, string UrlConsultaProd,
    string UrlStatusHom, string UrlStatusProd,
    string UrlInutilizacaoHom, string UrlInutilizacaoProd,
    string UrlEventoHom, string UrlEventoProd,
    string UrlNfceAutorizacaoHom, string UrlNfceAutorizacaoProd,
    string UrlNfceQrCodeHom, string UrlNfceQrCodeProd
);

public static class UfWebserviceConfig
{
    // URLs de autorização/consulta/status/inutilização/evento do grupo SVRS
    // -- idênticas pra qualquer UF que usa a SVRS (compartilhado), só a URL
    // do QR Code varia por UF de verdade.
    private const string SvrsAutorizacaoHom = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeAutorizacao/NfeAutorizacao4.asmx";
    private const string SvrsAutorizacaoProd = "https://nfe.svrs.rs.gov.br/ws/NfeAutorizacao/NfeAutorizacao4.asmx";
    private const string SvrsConsultaHom = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";
    private const string SvrsConsultaProd = "https://nfe.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx";
    private const string SvrsStatusHom = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";
    private const string SvrsStatusProd = "https://nfe.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx";
    private const string SvrsInutilizacaoHom = "https://nfe-homologacao.svrs.rs.gov.br/ws/NfeInutilizacao/NfeInutilizacao4.asmx";
    private const string SvrsInutilizacaoProd = "https://nfe.svrs.rs.gov.br/ws/NfeInutilizacao/NfeInutilizacao4.asmx";
    private const string SvrsEventoHom = "https://nfe-homologacao.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";
    private const string SvrsEventoProd = "https://nfe.svrs.rs.gov.br/ws/recepcaoevento/recepcaoevento4.asmx";
    private const string SvrsNfceAutorizacaoHom = "https://nfce-homologacao.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx";
    private const string SvrsNfceAutorizacaoProd = "https://nfce.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx";

    private static UfWebserviceInfo Svrs(string codigoUf, string qrCodeHom, string qrCodeProd) => new(
        codigoUf,
        SvrsAutorizacaoHom, SvrsAutorizacaoProd,
        SvrsConsultaHom, SvrsConsultaProd,
        SvrsStatusHom, SvrsStatusProd,
        SvrsInutilizacaoHom, SvrsInutilizacaoProd,
        SvrsEventoHom, SvrsEventoProd,
        SvrsNfceAutorizacaoHom, SvrsNfceAutorizacaoProd,
        qrCodeHom, qrCodeProd);

    /// <summary>UF (sigla) -> config. Só entradas confirmadas contra fonte oficial
    /// da SEFAZ do estado (portal ENCAT cruzado com domínio .gov.br do próprio
    /// estado) -- ver comentário da classe. RN e RR ficam de fora de propósito:
    /// pesquisa encontrou sinais de URL em transição (RN) ou endereço frágil
    /// tipo IP numérico em homologação (RR), nenhum dos dois com confiança
    /// suficiente pra ir pra produção fiscal sem confirmação direta com a
    /// respectiva SEFAZ. Ambas caem no fallback PB via ResolverConfigUf até
    /// serem confirmadas -- nunca uma URL chutada.</summary>
    public static readonly IReadOnlyDictionary<string, UfWebserviceInfo> PorUf =
        new Dictionary<string, UfWebserviceInfo>
        {
            ["PB"] = Svrs("25", "http://www.sefaz.pb.gov.br/nfcehom", "http://www.sefaz.pb.gov.br/nfce"),
            ["AC"] = Svrs("12", "http://hml.sefaznet.ac.gov.br/nfce/qrcode?", "http://www.sefaznet.ac.gov.br/nfce/qrcode?"),
            // AL: mesma URL documentada pro ENCAT em hom e prod -- sem comunicado
            // oficial explícito confirmando isso, mas a URL em si foi achada
            // isolada e ativa no domínio oficial.
            ["AL"] = Svrs("27", "http://nfce.sefaz.al.gov.br/QRCode/consultarNFCe.jsp", "http://nfce.sefaz.al.gov.br/QRCode/consultarNFCe.jsp"),
            ["AP"] = Svrs("16", "https://www.sefaz.ap.gov.br/nfcehml/nfce.php", "https://www.sefaz.ap.gov.br/nfce/nfcep.php"),
            ["DF"] = Svrs("53", "http://www.fazenda.df.gov.br/nfce/qrcode?", "http://www.fazenda.df.gov.br/nfce/qrcode?"),
            ["ES"] = Svrs("32", "http://homologacao.sefaz.es.gov.br/ConsultaNFCe/", "http://app.sefaz.es.gov.br/ConsultaNFCe/"),
            ["PA"] = Svrs("15",
                "https://appnfc.sefa.pa.gov.br/portal-homologacao/view/consultas/nfce/nfceForm.seam",
                "https://appnfc.sefa.pa.gov.br/portal/view/consultas/nfce/nfceForm.seam"),
            ["RO"] = Svrs("11", "http://www.nfce.sefin.ro.gov.br/consultanfce/consulta.jsp", "http://www.nfce.sefin.ro.gov.br/consultanfce/consulta.jsp"),
            ["SC"] = Svrs("42", "https://hom.sat.sef.sc.gov.br/nfce/consulta?", "https://sat.sef.sc.gov.br/nfce/consulta?"),
            ["SE"] = Svrs("28", "http://www.hom.nfe.se.gov.br/nfce/qrcode?", "http://www.nfce.se.gov.br/nfce/qrcode?"),
            // TO: certificado TLS da URL de homologação estava expirado no
            // momento da verificação (problema operacional do estado, não
            // invalida o endereço em si).
            ["TO"] = Svrs("17", "http://homologacao.sefaz.to.gov.br/nfce/qrcode", "http://www.sefaz.to.gov.br/nfce/qrcode"),
        };

    /// <summary>Resolve pela UF, caindo no fallback (NFeOptions, historicamente PB) se
    /// a UF nao tiver entrada confirmada. Compartilhado entre NFeService,
    /// CancelamentoService, ManifestacaoService etc -- nao duplicar essa
    /// lógica por serviço.</summary>
    public static UfWebserviceInfo Resolve(string? uf, Jubilados.Application.Configuration.NFeOptions fallback)
    {
        if (!string.IsNullOrEmpty(uf) && PorUf.TryGetValue(uf, out var cfg))
            return cfg;

        return new UfWebserviceInfo(
            fallback.CodigoUF,
            fallback.SefazUrlHomologacao, fallback.SefazUrlProducao,
            fallback.SefazUrlConsultaHom, fallback.SefazUrlConsultaProd,
            fallback.SefazUrlStatusHom, fallback.SefazUrlStatusProd,
            fallback.SefazUrlInutilizacaoHom, fallback.SefazUrlInutilizacaoProd,
            fallback.SefazUrlEventoHom, fallback.SefazUrlEventoProd,
            fallback.UrlNfceAutorizacaoHom, fallback.UrlNfceAutorizacaoProd,
            fallback.UrlNfceQrCodeHom, fallback.UrlNfceQrCodeProd);
    }
}
