using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SetupHub.Nucleo;

namespace SetupHub.Servico;

// Outras contas e a rede são recusadas pelo próprio Windows, antes de qualquer mensagem chegar.
sealed class Servidor
{
    readonly Func<Conexao, Mensagem, Task<Mensagem>> tratar;
    readonly Func<string?> usuario;
    readonly List<Conexao> conexoes = new();
    volatile bool rodando;
    public Action<Conexao>? AoFechar;

    public Servidor(Func<Conexao, Mensagem, Task<Mensagem>> tratar, Func<string?> usuario) { this.tratar = tratar; this.usuario = usuario; }

    PipeSecurity Seguranca()
    {
        var s = new PipeSecurity();
        s.SetAccessRuleProtection(true, false);
        s.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        // Quem cria o canal precisa abrir novas instâncias dele.
        var dono = WindowsIdentity.GetCurrent().User;
        if (dono is not null && !dono.IsWellKnown(WellKnownSidType.LocalSystemSid))
            s.AddAccessRule(new PipeAccessRule(dono, PipeAccessRights.FullControl, AccessControlType.Allow));
        var sid = usuario();
        SecurityIdentifier? conta = null;
        if (!string.IsNullOrEmpty(sid)) { try { conta = new SecurityIdentifier(sid); } catch { } }
        conta ??= WindowsIdentity.GetCurrent().User;
        if (conta is not null && !conta.IsWellKnown(WellKnownSidType.LocalSystemSid))
            s.AddAccessRule(new PipeAccessRule(conta, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return s;
    }

    public void Iniciar()
    {
        rodando = true;
        for (int i = 0; i < 2; i++) _ = Task.Run(Aceitar);
    }

    async Task Aceitar()
    {
        while (rodando)
        {
            NamedPipeServerStream? p = null;
            try
            {
                p = NamedPipeServerStreamAcl.Create(Conexao.NomeCanal, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1 << 16, 1 << 16, Seguranca());
                await p.WaitForConnectionAsync().ConfigureAwait(false);
                var c = new Conexao(p);
                c.AoPedir = m => tratar(c, m);
                c.AoFechar = () => { lock (conexoes) conexoes.Remove(c); AoFechar?.Invoke(c); };
                lock (conexoes) conexoes.Add(c);
                _ = c.IniciarLeitura();
            }
            catch (Exception e)
            {
                try { p?.Dispose(); } catch { }
                if (rodando) { Registro.Erro("Canal", e); await Task.Delay(500).ConfigureAwait(false); }
            }
        }
    }

    public void Parar()
    {
        rodando = false;
        List<Conexao> copia;
        lock (conexoes) copia = conexoes.ToList();
        foreach (var c in copia) _ = c.DisposeAsync();
    }
}
