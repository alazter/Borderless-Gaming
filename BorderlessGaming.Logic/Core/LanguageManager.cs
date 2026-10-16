using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using BorderlessGaming.Logic.Models;
using BorderlessGaming.Logic.System;

namespace BorderlessGaming.Logic.Core
{
    public class LanguageManager
    {

        public static string CurrentCulture { get; set; }

        private static readonly HashSet<string> CultureNames = CreateCultureNames();

        private static readonly string _archiveName = "Languages.zip";

        private static Dictionary<string, Language> Languages { get; set; }

        private static HashSet<string> CreateCultureNames()
        {
            var cultureInfos = CultureInfo.GetCultures(CultureTypes.AllCultures)
                .Where(x => !string.IsNullOrEmpty(x.Name))
                .ToArray();
            var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            allNames.UnionWith(cultureInfos.Select(x => x.TwoLetterISOLanguageName));
            allNames.UnionWith(cultureInfos.Select(x => x.Name));
            return allNames;
        }

        private static bool CultureExists(string name)
        {
            return CultureNames.Contains(name);
        }

        public static string Data(string key)
        {
            key = key.ToLower();
            if (string.IsNullOrEmpty(CurrentCulture) || !Languages.ContainsKey(CurrentCulture))
            {
                var fallback = Languages.ContainsKey("en-US") ? "en-US" : Languages.Keys.FirstOrDefault();
                if (fallback != null)
                {
                    CurrentCulture = fallback;
                }
            }
            var lang = Languages[CurrentCulture];
            var data = lang.Data(key);
            if (string.IsNullOrWhiteSpace(data) && Languages.TryGetValue("en-US", out var enLang))
            {
                data = enLang.Data(key);
            }
            if (string.IsNullOrWhiteSpace(data))
            {
                MessageBox.Show($"{lang.Culture} is missing a translation for {key}");
                Environment.Exit(0);
            }
            return data;
        }

        private static void EnsureLanguageFilesExist()
        {
            try
            {
                if (!Directory.Exists(AppEnvironment.LanguagePath))
                {
                    Directory.CreateDirectory(AppEnvironment.LanguagePath);
                }

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidateArchives = new[]
                {
                    _archiveName,
                    Path.Combine(baseDir, _archiveName),
                    Path.Combine(baseDir, "Languages", _archiveName)
                };

                foreach (var archivePath in candidateArchives)
                {
                    if (File.Exists(archivePath))
                    {
                        try
                        {
                            Tools.ExtractZipFile(archivePath, string.Empty, AppEnvironment.LanguagePath);
                            break;
                        }
                        catch
                        {
                            // Continue to other fallback sources if zip extraction fails
                        }
                    }
                }

                // Copy any .lang files shipped in baseDir\Languages or culture subdirectories (e.g. baseDir\en-US\en-US.lang)
                var localLangDir = Path.Combine(baseDir, "Languages");
                if (Directory.Exists(localLangDir))
                {
                    foreach (var file in Directory.GetFiles(localLangDir, "*.lang"))
                    {
                        var dest = Path.Combine(AppEnvironment.LanguagePath, Path.GetFileName(file));
                        if (!File.Exists(dest))
                        {
                            File.Copy(file, dest, false);
                        }
                    }
                }

                if (Directory.Exists(baseDir))
                {
                    foreach (var file in Directory.GetFiles(baseDir, "*.lang", SearchOption.AllDirectories))
                    {
                        var dest = Path.Combine(AppEnvironment.LanguagePath, Path.GetFileName(file));
                        if (!File.Exists(dest))
                        {
                            File.Copy(file, dest, false);
                        }
                    }
                }

                // Final fallback: extract embedded Languages.zip resource from BorderlessGaming.Logic assembly
                if (Directory.GetFiles(AppEnvironment.LanguagePath, "*.lang").Length == 0)
                {
                    var assembly = typeof(LanguageManager).Assembly;
                    var resourceName = assembly.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith("Languages.zip", StringComparison.OrdinalIgnoreCase));
                    if (resourceName != null)
                    {
                        var tempZip = Path.Combine(AppEnvironment.LanguagePath, _archiveName);
                        using (var stream = assembly.GetManifestResourceStream(resourceName))
                        using (var fs = File.Create(tempZip))
                        {
                            stream?.CopyTo(fs);
                        }
                        Tools.ExtractZipFile(tempZip, string.Empty, AppEnvironment.LanguagePath);
                        File.Delete(tempZip);
                    }
                }
            }
            catch (Exception e)
            {
                if (!Directory.Exists(AppEnvironment.LanguagePath) ||
                    Directory.GetFiles(AppEnvironment.LanguagePath, "*.lang").Length == 0)
                {
                    MessageBox.Show("Failed to extract the language pack. Please report this: " + e.Message);
                    Environment.Exit(1);
                }
            }
        }

        public static void Load()
        {
            Languages = new Dictionary<string, Language>(StringComparer.OrdinalIgnoreCase);
            EnsureLanguageFilesExist();
            if (!Directory.Exists(AppEnvironment.LanguagePath))
            {
                MessageBox.Show("UI Translations are missing from disk.");
                Environment.Exit(1);
            }
            foreach (var langFile in Directory.GetFiles(AppEnvironment.LanguagePath, "*.lang"))
            {
                var culture = Path.GetFileNameWithoutExtension(langFile);
                if (culture != null && CultureExists(culture) && !Languages.ContainsKey(culture))
                {
                    var lang = new Language {Culture = culture};
                    lang.LoadData(langFile);
                    if (lang.LanguageData != null)
                    {
                        Languages.Add(culture, lang);
                    }
                }
            }
            if (Languages.Count <= 0)
            {
               MessageBox.Show($"No Langauges have been loaded! Ensure {AppEnvironment.LanguagePath} exist with at least one .lang file.");
               Environment.Exit(0);
            }
            var defaultLang = Languages.Values.FirstOrDefault(lang => lang.Culture.Equals(Config.Instance.AppSettings.DefaultCulture, StringComparison.OrdinalIgnoreCase))
                              ?? Languages.Values.FirstOrDefault(lang => lang.Culture.Equals("en-US", StringComparison.OrdinalIgnoreCase))
                              ?? Languages.Values.FirstOrDefault();
            defaultLang?.Set();
        }

        public static void Setup(ToolStripMenuItem toolStripLanguages)
        {

            foreach (var lang in Languages.Values)
            {
                var item = toolStripLanguages.DropDownItems.Add(new ToolStripMenuItem
                {
                    Text = lang.ToString(),
                    CheckOnClick = true,
                    Checked = IsDefault(lang.DisplayName)
                });
                toolStripLanguages.DropDownItems[item].Click += (s, ea) =>
                {
                    var tt = (ToolStripMenuItem)s;
                    if (!tt.Checked)
                    {
                        if (IsDefault(tt.Text) && !LanguageSelected(toolStripLanguages.DropDownItems))
                        {
                            tt.Checked = true;
                        }
                        return;
                    }
                    foreach (ToolStripMenuItem dropItem in toolStripLanguages.DropDownItems)
                    {
                        if (dropItem != tt)
                        {
                            dropItem.Checked = false;
                        }
                    }
                    SetDefaultLanguage(tt.Text);
                };
            }
        }

        private static bool LanguageSelected(ToolStripItemCollection dropDownItems)
        {
            var anyChecked = false;
            foreach (ToolStripMenuItem dropItem in dropDownItems)
            {
                anyChecked = dropItem.Checked;
            }
            return anyChecked;
        }

        private static bool IsDefault(string displayName)
        {
            try
            {
                var defaultCulture = Config.Instance.AppSettings.DefaultCulture;
                var langauge = Languages.Values.FirstOrDefault(lang => lang.DisplayName.Equals(displayName));
                return langauge != null && langauge.Culture.Equals(defaultCulture);
            }
            catch
            {

                return false;
            }
        }

        private static void SetDefaultLanguage(string tsiText)
        {
            var langauge = Languages.Values.FirstOrDefault(lang => lang.DisplayName.Equals(tsiText));
            if (langauge != null)
            {
                Config.Instance.AppSettings.DefaultCulture = langauge.Culture;
                Config.Save();
                langauge.Set();
                var dialogResult = MessageBox.Show(Data("settingConfirmationPrompt"), Data("settingConfirmationTitle"),
                    MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.Yes)
                {
                    string batchContent = "/c \"@ECHO OFF & timeout /t 6 > nul & start \"\" \"$[APPPATH]$\" & exit\"";
                    batchContent = batchContent.Replace("$[APPPATH]$", Application.ExecutablePath);
                    Process.Start("cmd", batchContent);
                    Application.Exit();
                }
            }
        }
    }
}