using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using SetupHub.Janela.Agente;
using SetupHub.Janela.Kit;
using SetupHub.Nucleo;

namespace SetupHub.Janela.Telas;

// "Todos os jogos" (nulo) edita os ajustes globais.
static class AcoesJogos
{
    public static readonly (string Chave, string Nome, string Desc)[] Chaves =
    {
        ("antiLag", "Anti-Lag", "reduz a latência de entrada"),
        ("boost", "Boost", "reduz a resolução em movimentos rápidos"),
        ("chill", "Chill", "reduz quadros em cenas estáticas"),
        ("enhancedSync", "Enhanced Sync", "elimina o rasgo de imagem sem somar latência"),
        ("rsr", "Radeon Super Resolution", "renderiza em resolução menor e amplia"),
        ("afmf", "Fluid Motion Frames", "gera quadros intermediários"),
    };

    public static readonly (string Leitura, string Nome)[] Sobreposicao =
    {
        ("hora", "Hora"), ("fps", "Quadros por segundo"), ("frametime", "Tempo de quadro"),
        ("cpu.temp", "Temperatura do processador"), ("cpu.clock", "Clock do processador"), ("cpu.uso", "Uso do processador"), ("cpu.ventoinha", "Fan do processador"),
        ("gpu.temp", "Temperatura da placa"), ("gpu.clock", "Clock da placa"), ("gpu.uso", "Uso da placa"), ("gpu.consumo", "Consumo da placa"), ("gpu.ventoinha", "Fan da placa"),
        ("memoria.uso", "Memória RAM"),
    };

    public static string TextoPrevia(Sobreposicao s, Estado? e) =>
        Rtss.Montar(s, e ?? new Estado(), e?.Fps, e?.Fps is double f && f > 0 ? 1000 / f : null);

    public static readonly (string, string)[] Fontes = Validacao.FontesSobreposicao.Select(f => (f, f)).ToArray();

    // Unispace vem com o RivaTuner e pode não estar instalada no Windows.
    public static FontFamily FontePrevia(string nome)
    {
        var pasta = Path.Combine(Rtss.Pasta, "Fonts");
        return nome == "Unispace" && File.Exists(Path.Combine(pasta, "unispace bd.ttf")) ? new FontFamily(new Uri(pasta + "\\"), "./#Unispace") : new FontFamily(nome);
    }

    public static void PintarPrevia(Sobreposicao s, TextBlock texto, Border caixa)
    {
        texto.FontFamily = FontePrevia(s.Fonte);
        texto.Foreground = Paleta.Cor(Validacao.CorValida(s.Cor) ? s.Cor : "#FF8000");
        caixa.Background = Brushes.Transparent;
        texto.Effect = s.Fundo ? new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, ShadowDepth = 1.5, BlurRadius = 0, Opacity = 1, Direction = 315 } : null;
    }

    // A tela do RivaTuner é de processo elevado: só a tarefa criada na instalação consegue trazê-la sem pedir permissão.
    public static async Task TelaRivaTuner()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", $"/run /tn \"{Produto.TarefaTelaRivaTuner}\"") { UseShellExecute = false, CreateNoWindow = true })!;
            await p.WaitForExitAsync();
            if (p.ExitCode != 0) App.Janela?.Aviso("Tarefa de abertura do RivaTuner ausente. Reinstale o SetupHub.", true);
        }
        catch { App.Janela?.Aviso("Falha ao abrir o RivaTuner.", true); }
    }

    public static readonly (string Campo, string Nome, string Desc)[] Windows =
    {
        ("modoJogo", "Modo de jogo do Windows", "prioriza recursos para o jogo em execução"),
        ("agendamentoGpu", "Agendamento pela placa de vídeo", "reduz a latência; requer reinicialização"),
        ("otimizacaoJanela", "Otimização de jogos em janela", "desempenho de tela cheia em modo janela"),
        ("vrrJanela", "Taxa variável em janela", "FreeSync em jogos no modo janela"),
        ("gravacaoFundo", "Gravação da Xbox Game Bar", "gravação de clipes e capturas durante o jogo"),
        ("precisaoPonteiro", "Precisão do ponteiro", "aceleração do mouse do Windows"),
        ("transparencia", "Transparência do Windows", "efeito translúcido nas janelas"),
        ("naoPerturbeEmJogo", "Não perturbe durante o jogo", "silencia notificações durante o jogo"),
    };

    public static bool? Chave(Ajustes3D a, string c) => c switch
    {
        "antiLag" => a.AntiLag, "boost" => a.Boost, "chill" => a.Chill, "enhancedSync" => a.EnhancedSync, "rsr" => a.Rsr, "afmf" => a.Afmf, _ => null,
    };

    public static void Chave(Ajustes3D a, string c, bool v)
    {
        switch (c)
        {
            case "antiLag": a.AntiLag = v; break;
            case "boost": a.Boost = v; break;
            case "chill": a.Chill = v; break;
            case "enhancedSync": a.EnhancedSync = v; break;
            case "rsr": a.Rsr = v; break;
            case "afmf": a.Afmf = v; break;
        }
    }

    // Do jogo, senão o global, senão o do driver agora.
    public static bool Efetivo(Config c, PerfilJogo? j, string chave, Faixas? f)
    {
        var v = (j?.Graficos is { } g ? Chave(g, chave) : null) ?? Chave(c.Jogos.Global, chave);
        return v ?? f?.Campos.GetValueOrDefault("graficos." + chave)?.Atual == 1;
    }

    public static bool Disponivel(Faixas? f, string chave) => f?.Campos.GetValueOrDefault("graficos." + chave) is { Disponivel: true };

    public static Task<bool> Graficos(string? exe, Action<Ajustes3D> mudar, string? ok = null)
    {
        var c = App.Lig.Config;
        if (c is null) return Task.FromResult(false);
        var n = Json.Copia(c);
        if (exe is null) mudar(n.Jogos.Global);
        else
        {
            var j = n.Jogos.Lista.FirstOrDefault(x => x.Executavel == exe);
            if (j is null) return Task.FromResult(false);
            j.Graficos ??= new Ajustes3D();
            mudar(j.Graficos);
        }
        return Enviar(n, ok);
    }

    public static Task<bool> Jogo(string exe, Action<PerfilJogo> mudar, string? ok = null)
    {
        var c = App.Lig.Config;
        if (c is null) return Task.FromResult(false);
        var n = Json.Copia(c);
        var j = n.Jogos.Lista.FirstOrDefault(x => x.Executavel == exe);
        if (j is null) return Task.FromResult(false);
        mudar(j);
        return Enviar(n, ok);
    }

    public static async Task<bool> Enviar(Config n, string? ok = null)
    {
        var r = await App.Lig.Pedir("config.gravar", Ligacao.Args(("config", Json.Elemento(n)), ("origem", "jogos")));
        if (!r.Ok) { App.Janela?.Aviso(r.Erro ?? "Recusado.", true); return false; }
        var res = Json.Ler<ResultadoAplicar>(r.Dados);
        if (res is { Falhas.Count: > 0 }) App.Janela?.Aviso("Falhou: " + string.Join("; ", res.Falhas), true);
        else if (ok is not null) App.Janela?.Aviso(ok);
        return true;
    }

    public static async Task<string?> Adicionar()
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Programas (*.exe)|*.exe", Title = "Escolha o programa do jogo" };
        if (d.ShowDialog() != true) return null;
        var exe = d.FileName;
        string nome;
        try { var info = FileVersionInfo.GetVersionInfo(exe); nome = string.IsNullOrWhiteSpace(info.ProductName) ? Path.GetFileNameWithoutExtension(exe) : info.ProductName.Trim(); }
        catch { nome = Path.GetFileNameWithoutExtension(exe); }
        var c = App.Lig.Config;
        if (c is null) return null;
        if (c.Jogos.Lista.Any(j => string.Equals(Path.GetFileName(j.Executavel), Path.GetFileName(exe), StringComparison.OrdinalIgnoreCase)))
        {
            App.Janela?.Aviso($"{nome} já está na lista.");
            return exe;
        }
        var n = Json.Copia(c);
        n.Jogos.Lista.Add(new PerfilJogo { Nome = nome, Executavel = exe, Perfil = n.Perfis.Any(p => p.Nome == "Desempenho") ? "Desempenho" : null });
        return await Enviar(n, $"{nome} adicionado. O perfil é aplicado ao abrir o jogo.") ? exe : null;
    }

    public static Task<bool> Remover(string exe)
    {
        var c = App.Lig.Config;
        if (c is null) return Task.FromResult(false);
        var n = Json.Copia(c);
        n.Jogos.Lista.RemoveAll(j => j.Executavel == exe);
        return Enviar(n, "Jogo removido da lista.");
    }

    public static ConfigWindows WindowsAgora() => WindowsUsuario.Ler();

    public static bool? ValorWindows(ConfigWindows w, string campo) => campo switch
    {
        "modoJogo" => w.ModoJogo, "agendamentoGpu" => w.AgendamentoGpu, "otimizacaoJanela" => w.OtimizacaoJanela, "vrrJanela" => w.VrrJanela,
        "gravacaoFundo" => w.GravacaoFundo, "precisaoPonteiro" => w.PrecisaoPonteiro, "transparencia" => w.Transparencia, "naoPerturbeEmJogo" => w.NaoPerturbeEmJogo, _ => null,
    };
}
