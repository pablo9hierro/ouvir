namespace Jubilados.Domain.Entities;

public class Usuario
{
    public Guid     Id        { get; set; }
    public string   Email     { get; set; } = string.Empty;
    public string   SenhaHash { get; set; } = string.Empty;
    public DateTime CriadoEm  { get; set; }
}
