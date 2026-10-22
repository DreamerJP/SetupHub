using System.IO;
using System.Text.Json;
using SetupHub.Nucleo;
using Microsoft.Web.WebView2.Core;

namespace SetupHub.Janela.Agente;

// O site da MSI recusa programas comuns: a consulta vai pelo motor do Edge numa janela invisível.
static class Atualizacoes
{
    public const string Produto = "B850-GAMING-PLUS-WIFI";
    public const string PaginaDownloads = "https://www.msi.com/Motherboard/B850-GAMING-PLUS-WIFI/support";

    public static async Task<List<Atualizacao>> Buscar(IntPtr janela, string biosInstalada, string dataBios)
    {
        var pasta = Path.Combine(Caminhos.Usuario, "navegador");
        Directory.CreateDirectory(pasta);
        var env = await CoreWebView2Environment.CreateAsync(null, pasta);
        var ctl = await env.CreateCoreWebView2ControllerAsync(janela);
        try
        {
            ctl.IsVisible = false;
            var lista = new List<Atualizacao>();
            foreach (var tipo in new[] { "bios", "driver" })
            {
                var json = await Ler(ctl.CoreWebView2, $"https://www.msi.com/api/v1/product/support/panel?product={Produto}&type={tipo}");
                lista.AddRange(Interpretar(json, tipo, biosInstalada, dataBios));
            }
            return lista;
        }
        finally { ctl.Close(); }
    }

    static async Task<string> Ler(CoreWebView2 w, string url)
    {
        var pronto = new TaskCompletionSource<bool>();
        void Fim(object? s, CoreWebView2NavigationCompletedEventArgs e) => pronto.TrySetResult(e.IsSuccess);
        w.NavigationCompleted += Fim;
        try
        {
            w.Navigate(url);
            if (await Task.WhenAny(pronto.Task, Task.Delay(30000)) != pronto.Task) throw new ErroSetupHub("O site da MSI não respondeu em 30 s.");
            if (!pronto.Task.Result) throw new ErroSetupHub("O site da MSI não abriu.");
            var bruto = await w.ExecuteScriptAsync("document.body.innerText");
            return JsonSerializer.Deserialize(bruto, SetupHubJson.Default.String) ?? "";
        }
        finally { w.NavigationCompleted -= Fim; }
    }

    static List<Atualizacao> Interpretar(string texto, string tipo, string biosInstalada, string dataBios)
    {
        var r = new List<Atualizacao>();
        if (texto.StartsWith("<") || texto.Contains("Access Denied")) throw new ErroSetupHub("O site da MSI recusou a consulta.");
        using var doc = JsonDocument.Parse(texto);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("result", out var res) || res.ValueKind != JsonValueKind.Object
            || !res.TryGetProperty("downloads", out var dl) || dl.ValueKind != JsonValueKind.Object) return r;
        // A data da BIOS instalada chega como dia/mês/ano (o serviço converte a do Windows).
        if (!DateTime.TryParseExact(dataBios, new[] { "dd/MM/yyyy", "MM/dd/yyyy" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var instalada))
            instalada = default;
        foreach (var grupo in dl.EnumerateObject())
        {
            if (grupo.Value.ValueKind != JsonValueKind.Array) continue;
            int n = 0;
            foreach (var item in grupo.Value.EnumerateArray())
            {
                // A lista de drivers traz grupos só com nomes ("type_title", "os") junto dos downloads.
                if (item.ValueKind != JsonValueKind.Object) continue;
                string Campo(string k) => item.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var a = new Atualizacao
                {
                    Tipo = tipo == "bios" ? "BIOS" : grupo.Name, Nome = Campo("download_title"), Versao = Campo("download_version"),
                    Data = Campo("download_release"), Endereco = Campo("download_url"),
                };
                if (DateTime.TryParse(a.Data, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
                {
                    if (tipo == "bios" && instalada != default) a.Nova = d.Date > instalada.Date;
                    a.Data = d.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
                }
                // O site chama a BIOS pelo arquivo (7E56v1AA3); o Windows, pela versão (1.AA3, e 1.A90 para o arquivo 7E56v1A9).
                if (tipo == "bios" && a.Versao.IndexOf('v') is int iv and > 0 && a.Versao.Length > iv + 2)
                {
                    var resto = a.Versao[(iv + 2)..];
                    a.Versao = a.Versao[iv + 1] + "." + resto.PadRight(3, '0');
                }
                r.Add(a);
                if (++n >= (tipo == "bios" ? 3 : 2)) break;
            }
        }
        return r;
    }
}
