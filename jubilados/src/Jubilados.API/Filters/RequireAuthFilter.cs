using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jubilados.API.Filters;

/// <summary>
/// Achado de auditoria: NFeController/EmpresaController/ProdutoController/
/// ClienteController não tinham NENHUMA autenticação -- qualquer um que
/// soubesse (ou adivinhasse) um EmpresaId GUID podia emitir nota, trocar
/// certificado ou ler dados de qualquer empresa, sem token nenhum.
///
/// Este filtro fecha só isso (autenticação, não autorização por-empresa):
/// aceita OU um header X-Internal-Key válido (chamada servidor-a-servidor
/// confiável, ex.: ecommerce-api do Resolutoo) OU um JWT autenticado
/// (usuário logado no próprio app Jubilados, fluxo que já existia).
///
/// NÃO faz cross-check "esse usuário é dono desta EmpresaId" -- essa
/// checagem não existe hoje em lugar nenhum do código pra nenhum usuário
/// logado, e implementar isso corretamente por controller (cada um usa o
/// id da rota com um significado diferente -- em EmpresaController é o
/// próprio EmpresaId, em ProdutoController é o ProdutoId) é maior que o
/// escopo desta correção pontual. Documentado como limitação conhecida no
/// relatório final da integração.
/// </summary>
public class RequireAuthFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;

        var internalKey = Environment.GetEnvironmentVariable("INTERNAL_API_KEY");
        var provided = http.Request.Headers["X-Internal-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(internalKey) && provided == internalKey)
            return;

        if (http.User.Identity?.IsAuthenticated == true)
            return;

        context.Result = new UnauthorizedObjectResult(new { error = "não autenticado" });
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
