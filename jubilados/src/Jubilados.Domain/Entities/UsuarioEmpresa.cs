namespace Jubilados.Domain.Entities;

public class UsuarioEmpresa
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SupabaseUserId { get; set; }
    public Guid? EmpresaId { get; set; }
    public bool OnboardingConcluido { get; set; } = false;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Empresa? Empresa { get; set; }
}
