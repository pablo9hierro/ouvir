namespace Jubilados.Infrastructure.Services;

/// <summary>Grupo &lt;card&gt; da NFC-e/NF-e (NT 2025.001, rejeição 391/392) --
/// facultativo por UF (cada SEFAZ ativa quando quer, sem aviso público
/// centralizado confirmado; PB já ativa desde 2024), então sempre
/// preenchido quando o dado existe, em vez de tentar adivinhar quais UFs
/// exigem. Só o pagamento vindo de gateway integrado (Mercado Pago) tem
/// esse dado -- tpIntegra=1 é condicionado à origem real do pagamento, não
/// inventado.</summary>
public static class CardGroupBuilder
{
    /// CNPJ da Mercado Pago Instituição de Pagamento Ltda (credenciadora
    /// real das transações via Point/Checkout Transparente) -- confirmado
    /// via Serasa/Portal da Transparência, não o CNPJ da empresa emitente.
    public const string MercadoPagoCnpj = "10573521000191";

    /// Tabela nacional de bandeiras (tBand) -- mapeia `payment_method_id` da
    /// Mercado Pago pro código oficial. Bandeira fora do mapa cai em "99"
    /// (Outros) em vez de falhar a emissão.
    public static readonly IReadOnlyDictionary<string, string> TBandPorBandeiraMp =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["visa"] = "01",
            ["master"] = "02",
            ["amex"] = "03",
            ["diners"] = "05",
            ["elo"] = "06",
            ["hipercard"] = "07",
            ["cabal"] = "09",
        };

    private static string XmlEnc(string v) => System.Security.SecurityElement.Escape(v)!;

    /// Retorna o fragmento XML `<card>...</card>` pronto, ou string vazia
    /// quando não se aplica (forma de pagamento não é cartão/PIX dinâmico,
    /// ou não há código de autorização real).
    public static string Montar(string formaPagamento, string? cardBrand, string? cardAuthorizationCode)
    {
        var aplica = formaPagamento is "03" or "04" or "17";
        if (!aplica || string.IsNullOrWhiteSpace(cardAuthorizationCode))
            return string.Empty;

        var tBand = !string.IsNullOrWhiteSpace(cardBrand) && TBandPorBandeiraMp.TryGetValue(cardBrand.Trim(), out var codigo)
            ? codigo
            : "99";
        return "<card><tpIntegra>1</tpIntegra>" +
               $"<CNPJ>{MercadoPagoCnpj}</CNPJ>" +
               $"<tBand>{tBand}</tBand>" +
               $"<cAut>{XmlEnc(cardAuthorizationCode.Trim())}</cAut></card>";
    }
}
