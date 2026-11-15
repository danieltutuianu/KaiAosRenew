using System;

namespace KaiAosRenew
{
    /// <summary>
    /// Short DE→UI-language replacements for Desktop KeyReader Plus log bodies.
    /// German UI leaves text unchanged.
    /// </summary>
    internal static class KeyReaderLogLocalize
    {
        // Longest phrases first.
        private static readonly string[] DePhrases =
        {
            "erfolgreich geschrieben",
            "Key data available!",
            "Schlüsseldaten",
            "nicht möglich",
            "keine Daten",
            "fehlgeschlagen",
            "erfolgreich",
            "geschrieben",
            "verfügbar",
            "Verbindung",
            "Warnung",
            "Fehler",
            "Datei",
            "gelesen"
        };

        private static readonly string[] EnPhrases =
        {
            "written successfully",
            "Key data available!",
            "key data",
            "not possible",
            "no data",
            "failed",
            "successfully",
            "written",
            "available",
            "connection",
            "Warning",
            "Error",
            "file",
            "read"
        };

        private static readonly string[] RoPhrases =
        {
            "scris cu succes",
            "Date cheie disponibile!",
            "date cheie",
            "nu este posibil",
            "fără date",
            "eșuat",
            "cu succes",
            "scris",
            "disponibil",
            "conexiune",
            "Avertisment",
            "Eroare",
            "fișier",
            "citit"
        };

        public static string LocalizeBody(string body)
        {
            if (string.IsNullOrEmpty(body)) return body;
            var lang = UiStrings.Language;
            if (string.Equals(lang, "de", StringComparison.OrdinalIgnoreCase))
                return body;

            string[] dest;
            if (string.Equals(lang, "ro", StringComparison.OrdinalIgnoreCase))
                dest = RoPhrases;
            else
                dest = EnPhrases;

            var result = body;
            for (var i = 0; i < DePhrases.Length; i++)
            {
                result = ReplaceIgnoreCase(result, DePhrases[i], dest[i]);
            }
            return result;
        }

        private static string ReplaceIgnoreCase(string hay, string needle, string replacement)
        {
            if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle)) return hay;
            var idx = 0;
            while (idx < hay.Length)
            {
                var found = hay.IndexOf(needle, idx, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                hay = hay.Substring(0, found) + replacement + hay.Substring(found + needle.Length);
                idx = found + replacement.Length;
            }
            return hay;
        }
    }
}
