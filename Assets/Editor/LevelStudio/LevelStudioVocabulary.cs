using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    /// <summary>Editor-only reader for the documented enemy vocabulary. The browser never owns aliases.</summary>
    public sealed class LevelStudioVocabulary
    {
        readonly Dictionary<string, string[]> enemyTerms = new Dictionary<string, string[]>(StringComparer.Ordinal);
        public string diagnostic { get; private set; }

        public static LevelStudioVocabulary Load()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projectRoot, "docs", "LEVEL-VOCABULARY.md");
            if (!File.Exists(path)) return WithDiagnostic("Vocabulary file is missing: " + path);
            try
            {
                string markdown = File.ReadAllText(path);
                const string marker = "<!-- dashboard-level-vocabulary -->";
                int markerIndex = markdown.IndexOf(marker, StringComparison.Ordinal);
                if (markerIndex < 0) return WithDiagnostic("Vocabulary marker is missing.");
                int jsonStart = markdown.IndexOf('{', markerIndex + marker.Length);
                string json = ExtractObject(markdown, jsonStart);
                if (string.IsNullOrEmpty(json)) return WithDiagnostic("Vocabulary JSON is missing or incomplete.");
                int enemyIndex = json.IndexOf("\"enemyTerms\"", StringComparison.Ordinal);
                int enemyStart = enemyIndex < 0 ? -1 : json.IndexOf('{', enemyIndex);
                string enemyJson = ExtractObject(json, enemyStart);
                return string.IsNullOrEmpty(enemyJson) ? WithDiagnostic("Vocabulary enemyTerms JSON is missing.") : ParseEnemyTerms(enemyJson);
            }
            catch (Exception e) { return WithDiagnostic("Could not read vocabulary: " + e.Message); }
        }

        public static LevelStudioVocabulary ParseEnemyTerms(string enemyTermsJson)
        {
            var vocabulary = new LevelStudioVocabulary();
            if (string.IsNullOrWhiteSpace(enemyTermsJson)) { vocabulary.diagnostic = "Vocabulary enemyTerms JSON is empty."; return vocabulary; }
            try
            {
                int cursor = 0;
                while (cursor < enemyTermsJson.Length)
                {
                    Match key = new Regex("\\\"(?<key>[^\\\"]+)\\\"\\s*:", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Match(enemyTermsJson, cursor);
                    if (!key.Success) break;
                    int keyAt = key.Index;
                    int objectStart = enemyTermsJson.IndexOf('{', keyAt + key.Length);
                    if (objectStart < 0) break;
                    string entry = ExtractObject(enemyTermsJson, objectStart);
                    if (string.IsNullOrEmpty(entry)) throw new InvalidDataException("An enemy term entry is incomplete.");
                    Match canonical = Regex.Match(entry, "\\\"canonical\\\"\\s*:\\s*\\\"(?<value>[^\\\"]*)\\\"", RegexOptions.CultureInvariant);
                    var terms = new List<string>();
                    if (canonical.Success) terms.Add(canonical.Groups["value"].Value);
                    Match aliases = Regex.Match(entry, "\\\"aliases\\\"\\s*:\\s*\\[(?<value>.*?)\\]", RegexOptions.CultureInvariant | RegexOptions.Singleline);
                    if (aliases.Success)
                        foreach (Match alias in Regex.Matches(aliases.Groups["value"].Value, "\\\"(?<value>[^\\\"]*)\\\"", RegexOptions.CultureInvariant))
                            terms.Add(alias.Groups["value"].Value);
                    vocabulary.enemyTerms[key.Groups["key"].Value] = terms.ToArray();
                    cursor = objectStart + entry.Length;
                }
                if (vocabulary.enemyTerms.Count == 0) vocabulary.diagnostic = "Vocabulary enemyTerms JSON contains no readable entries.";
            }
            catch (Exception e) { vocabulary.enemyTerms.Clear(); vocabulary.diagnostic = "Vocabulary enemyTerms JSON is invalid: " + e.Message; }
            return vocabulary;
        }

        public IEnumerable<string> EnemyTermsFor(string key)
        {
            string[] terms;
            return !string.IsNullOrEmpty(key) && enemyTerms.TryGetValue(key, out terms) ? terms : new string[0];
        }

        static LevelStudioVocabulary WithDiagnostic(string text)
        {
            return new LevelStudioVocabulary { diagnostic = text };
        }

        static string ExtractObject(string text, int start)
        {
            if (start < 0 || start >= text.Length || text[start] != '{') return null;
            bool quoted = false, escaped = false;
            int depth = 0;
            for (int i = start; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '\"') quoted = false;
                    continue;
                }
                if (c == '\"') quoted = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return text.Substring(start, i - start + 1);
            }
            return null;
        }
    }
}
