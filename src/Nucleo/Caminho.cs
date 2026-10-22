using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SetupHub.Nucleo;

// Caminho como "ventoinhas.saidas[sys1].fixo": entre colchetes, a posição na lista ou o id/nome do item.
public static class Caminho
{
    public static Config Definir(Config c, string caminho, string valor)
    {
        var raiz = JsonNode.Parse(Json.Texto(c, true))!;
        var (pai, chave, indice) = Navegar(raiz, caminho, criar: true);
        JsonNode? atual = indice >= 0 ? pai.AsArray()[indice] : pai.AsObject()[chave!];
        var novo = Converter(valor, atual);
        if (indice >= 0) pai.AsArray()[indice] = novo;
        else
        {
            var obj = pai.AsObject();
            if (!obj.ContainsKey(chave!)) throw new ErroSetupHub($"O campo \"{chave}\" não existe em \"{caminho}\".");
            obj[chave!] = novo;
        }
        try { return Json.DeTexto<Config>(raiz.ToJsonString()) ?? throw new ErroSetupHub("Configuração vazia."); }
        catch (JsonException e) { throw new ErroSetupHub($"Valor \"{valor}\" não serve para \"{caminho}\": {Simplificar(e.Message)}"); }
    }

    public static string Ler(Config c, string caminho)
    {
        var raiz = JsonNode.Parse(Json.Texto(c, true))!;
        if (string.IsNullOrWhiteSpace(caminho)) return raiz.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var (pai, chave, indice) = Navegar(raiz, caminho, criar: false);
        JsonNode? n = indice >= 0 ? pai.AsArray()[indice] : pai.AsObject()[chave!];
        return n?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null";
    }

    public static List<string> Campos(Config c)
    {
        var lista = new List<string>();
        void Andar(JsonNode? n, string prefixo)
        {
            switch (n)
            {
                case JsonObject o:
                    foreach (var (k, v) in o) Andar(v, prefixo.Length == 0 ? k : prefixo + "." + k);
                    break;
                case JsonArray a:
                    for (int i = 0; i < a.Count; i++)
                    {
                        var item = a[i];
                        string sel = item is JsonObject io && (io["id"] ?? io["nome"]) is JsonValue jv && jv.TryGetValue<string>(out var s) && s.Length > 0 ? s : i.ToString();
                        if (item is JsonObject) Andar(item, $"{prefixo}[{sel}]");
                        else { lista.Add(prefixo); break; }
                    }
                    if (a.Count == 0) lista.Add(prefixo);
                    break;
                default: lista.Add(prefixo); break;
            }
        }
        Andar(JsonNode.Parse(Json.Texto(c, true)), "");
        return lista;
    }

    static (JsonNode pai, string? chave, int indice) Navegar(JsonNode raiz, string caminho, bool criar)
    {
        var partes = Dividir(caminho);
        JsonNode atual = raiz;
        for (int i = 0; i < partes.Count; i++)
        {
            var (nome, sel) = partes[i];
            bool ultimo = i == partes.Count - 1;
            if (atual is not JsonObject obj) throw new ErroSetupHub($"\"{caminho}\": \"{nome}\" não é um grupo de campos.");
            var chave = obj.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, nome, StringComparison.OrdinalIgnoreCase));
            if (chave is null) throw new ErroSetupHub($"O campo \"{nome}\" não existe em \"{caminho}\". Veja os campos com: setuphub listar campos");
            if (sel is null)
            {
                if (ultimo) return (obj, chave, -1);
                var prox = obj[chave];
                if (prox is null)
                {
                    if (!criar) throw new ErroSetupHub($"\"{nome}\" está vazio.");
                    throw new ErroSetupHub($"\"{nome}\" está vazio; defina o grupo inteiro em JSON primeiro.");
                }
                atual = prox;
                continue;
            }
            if (obj[chave] is not JsonArray lista) throw new ErroSetupHub($"\"{nome}\" não é uma lista.");
            int idx = AcharItem(lista, sel);
            if (idx < 0) throw new ErroSetupHub($"Não há \"{sel}\" em \"{nome}\".");
            if (ultimo) return (lista, null, idx);
            atual = lista[idx]!;
        }
        throw new ErroSetupHub("Caminho vazio.");
    }

    static int AcharItem(JsonArray lista, string sel)
    {
        if (int.TryParse(sel, out var n) && n >= 0 && n < lista.Count && !(lista[n] is JsonObject o0 && o0["id"] is not null && Texto(o0["id"]) != sel && lista.Any(x => x is JsonObject ox && Texto(ox["id"]) == sel))) return n;
        for (int i = 0; i < lista.Count; i++)
        {
            if (lista[i] is not JsonObject o) continue;
            foreach (var campo in new[] { "id", "nome", "executavel", "sensor", "tipo" })
                if (string.Equals(Texto(o[campo]), sel, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    static List<(string nome, string? sel)> Dividir(string caminho)
    {
        var r = new List<(string, string?)>();
        int i = 0;
        while (i < caminho.Length)
        {
            int ini = i;
            while (i < caminho.Length && caminho[i] != '.' && caminho[i] != '[') i++;
            string nome = caminho[ini..i];
            string? sel = null;
            if (i < caminho.Length && caminho[i] == '[')
            {
                int fim = caminho.IndexOf(']', i);
                if (fim < 0) throw new ErroSetupHub($"Falta fechar o colchete em \"{caminho}\".");
                sel = caminho[(i + 1)..fim].Trim('"', '\'');
                i = fim + 1;
            }
            if (i < caminho.Length && caminho[i] == '.') i++;
            if (nome.Length == 0) throw new ErroSetupHub($"Caminho inválido: \"{caminho}\".");
            r.Add((nome, sel));
        }
        return r;
    }

    static JsonNode? Converter(string valor, JsonNode? atual)
    {
        var v = valor.Trim();
        var minus = v.ToLowerInvariant();
        if (minus is "nulo" or "null" or "padrao" or "padrão" or "") return null;
        bool eraBool = atual is JsonValue jb && (jb.GetValueKind() is JsonValueKind.True or JsonValueKind.False);
        if (minus is "sim" or "verdadeiro" or "true" || eraBool && minus is "ligado" or "ligar" or "on") return JsonValue.Create(true);
        if (minus is "nao" or "não" or "falso" or "false" || eraBool && minus is "desligado" or "desligar" or "off") return JsonValue.Create(false);
        if (atual is JsonValue jn && jn.GetValueKind() == JsonValueKind.Number || atual is null)
        {
            var num = v.Replace("%", "").Replace("°C", "").Replace("W", "").Replace("MHz", "").Trim();
            if (double.TryParse(num.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && num.Length > 0 && (char.IsDigit(num[0]) || num[0] == '-' || num[0] == '+'))
                return Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < int.MaxValue ? JsonValue.Create((long)Math.Round(d)) : JsonValue.Create(d);
        }
        if (v.StartsWith('{') || v.StartsWith('[') || v.StartsWith('"'))
        {
            try { return JsonNode.Parse(v); } catch (JsonException e) { throw new ErroSetupHub("JSON inválido: " + Simplificar(e.Message)); }
        }
        return JsonValue.Create(v);
    }

    static string Simplificar(string m)
    {
        int p = m.IndexOf(" Path:", StringComparison.Ordinal);
        return p > 0 ? m[..p] : m;
    }
}
