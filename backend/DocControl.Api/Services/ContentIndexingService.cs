using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocControl.Api.Services;

/// <summary>
/// Servicio de extracción e indexación de texto para documentos PDF y TXT (RF-19).
/// </summary>
public class ContentIndexingService
{
    private const int MaxCaracteresIndexados = 200_000;

    /// <summary>
    /// Extrae el texto seleccionable de un archivo físico (.txt o .pdf).
    /// Devuelve null si el archivo no existe, es un formato no textual o es un PDF escaneado/sin texto.
    /// </summary>
    public async Task<string?> ExtractTextAsync(string? filePath, string? extension = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        string ext = (!string.IsNullOrWhiteSpace(extension) ? extension : Path.GetExtension(filePath))
            .Trim()
            .ToLowerInvariant();

        if (!ext.StartsWith('.'))
            ext = "." + ext;

        if (ext == ".txt")
        {
            try
            {
                string raw = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
                return NormalizarYRecortar(raw);
            }
            catch
            {
                return null;
            }
        }

        if (ext == ".pdf")
        {
            try
            {
                return await Task.Run(() =>
                {
                    var sb = new StringBuilder();
                    using var document = PdfDocument.Open(filePath);
                    foreach (var page in document.GetPages())
                    {
                        var pageText = ContentOrderTextExtractor.GetText(page);
                        if (string.IsNullOrWhiteSpace(pageText))
                        {
                            pageText = string.Join(" ", page.GetWords().Select(w => w.Text));
                        }
                        if (string.IsNullOrWhiteSpace(pageText))
                        {
                            pageText = page.Text;
                        }

                        if (!string.IsNullOrWhiteSpace(pageText))
                        {
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(pageText);
                        }
                        if (sb.Length >= MaxCaracteresIndexados + 5000)
                            break;
                    }
                    return NormalizarYRecortar(sb.ToString());
                });
            }
            catch
            {
                // PDF escaneado, protegido o dañado: nunca rompe la subida
                return null;
            }
        }

        return null;
    }

    /// <summary>Normaliza texto removiendo diacríticos/acentos y convirtiendo a minúsculas para búsqueda insensible.</summary>
    public static string NormalizarParaBusqueda(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        var formD = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Verifica si <paramref name="texto"/> contiene <paramref name="consulta"/> ignorando mayúsculas y acentos.</summary>
    public static bool ContieneIgnorandoAcentos(string? texto, string? consulta)
    {
        if (string.IsNullOrWhiteSpace(texto) || string.IsNullOrWhiteSpace(consulta))
            return false;

        string normTexto = NormalizarParaBusqueda(texto);
        string normConsulta = NormalizarParaBusqueda(consulta.Trim());
        if (normConsulta.Length == 0)
            return false;

        return normTexto.Contains(normConsulta, StringComparison.Ordinal);
    }

    /// <summary>
    /// Construye un extracto (~140 caracteres) alrededor de la primera aparición de la consulta buscada,
    /// localizándola sin distinguir mayúsculas ni acentos y recortando sobre el texto original.
    /// </summary>
    public static string? ConstruirExtracto(string? textoIndexado, string? consulta, int longitudVentana = 140)
    {
        if (string.IsNullOrWhiteSpace(textoIndexado) || string.IsNullOrWhiteSpace(consulta))
            return null;

        string normQ = NormalizarParaBusqueda(consulta.Trim());
        if (normQ.Length == 0)
            return null;

        var normSb = new StringBuilder(textoIndexado.Length);
        var origIndices = new List<int>(textoIndexado.Length);

        for (int i = 0; i < textoIndexado.Length; i++)
        {
            string decomposed = textoIndexado[i].ToString().Normalize(NormalizationForm.FormD);
            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    normSb.Append(char.ToLowerInvariant(ch));
                    origIndices.Add(i);
                }
            }
        }

        int normIdx = normSb.ToString().IndexOf(normQ, StringComparison.Ordinal);
        if (normIdx < 0 || origIndices.Count == 0)
            return null;

        int matchStart = origIndices[normIdx];
        int endNormIdx = Math.Min(normIdx + normQ.Length - 1, origIndices.Count - 1);
        int matchEnd = origIndices[endNormIdx] + 1;

        int margenAntes = Math.Max(20, (longitudVentana - (matchEnd - matchStart)) / 2);
        int inicio = Math.Max(0, matchStart - margenAntes);
        int fin = Math.Min(textoIndexado.Length, inicio + longitudVentana);

        if (fin - inicio < longitudVentana && inicio > 0)
        {
            inicio = Math.Max(0, fin - longitudVentana);
        }

        string fragmento = textoIndexado[inicio..fin].Trim();
        string prefijo = inicio > 0 ? "... " : string.Empty;
        string sufijo = fin < textoIndexado.Length ? " ..." : string.Empty;

        return $"{prefijo}{fragmento}{sufijo}";
    }

    private static string? NormalizarYRecortar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        string limpio = Regex.Replace(texto, @"\s+", " ").Trim();
        if (limpio.Length == 0)
            return null;

        if (limpio.Length > MaxCaracteresIndexados)
            limpio = limpio[..MaxCaracteresIndexados];

        return limpio;
    }
}
