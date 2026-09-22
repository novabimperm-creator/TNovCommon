using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace TNovCommon.Help
{
    /// <summary>Раздел бандла: одна статья вики.</summary>
    public sealed class HelpBundleTopic
    {
        [JsonProperty("slug")]       public string Slug { get; set; }
        [JsonProperty("title")]      public string Title { get; set; }
        [JsonProperty("parentSlug")] public string ParentSlug { get; set; }
        [JsonProperty("sort")]       public int Sort { get; set; }
        [JsonProperty("file")]       public string File { get; set; }
        [JsonProperty("updatedAt")]  public string UpdatedAt { get; set; }
    }

    internal sealed class HelpBundleManifest
    {
        [JsonProperty("generated")] public string Generated { get; set; }
        [JsonProperty("source")]    public string Source { get; set; }
        [JsonProperty("rootSlug")]  public string RootSlug { get; set; }
        [JsonProperty("topics")]    public List<HelpBundleTopic> Topics { get; set; }
    }

    /// <summary>
    /// Бандл справки, вшитый в TNovCommon.dll (help.tnpack — zip из manifest.json,
    /// нормализованного XHTML и картинок). Собирается tools\Build-HelpBundle.ps1
    /// из вики плагина.
    ///
    /// Поток из GetManifestResourceStream — это UnmanagedMemoryStream над
    /// отображённым образом сборки: seekable и без копирования, поэтому отдаём
    /// его прямо в ZipArchive и не материализуем бандл в managed-памяти.
    /// ZipArchive не потокобезопасен, а автоконтекст может прийти с рабочего
    /// потока команды, поэтому весь доступ под одним lock.
    /// </summary>
    public static class HelpBundle
    {
        private const string ResourceName = "TNovCommon.Help.help.tnpack";

        private static readonly object Gate = new object();

        private static bool _initialized;
        private static Stream _stream;
        private static ZipArchive _archive;
        private static Dictionary<string, HelpBundleTopic> _bySlug;
        private static List<HelpBundleTopic> _ordered;

        /// <summary>Разделы бандла в порядке из вики. Пустой список, если бандла нет.</summary>
        public static IReadOnlyList<HelpBundleTopic> Topics
        {
            get
            {
                lock (Gate)
                {
                    EnsureLoaded();
                    return _ordered;
                }
            }
        }

        /// <summary>Есть ли в бандле статья для этого ключа функции.</summary>
        public static bool HasTopicFor(string funcName)
        {
            HelpBundleTopic topic;
            return TryGetTopicFor(funcName, out topic);
        }

        /// <summary>
        /// Ключ функции -> раздел бандла. Связка идёт через реестр HelpLinks:
        /// ключ -> slug -> статья.
        /// </summary>
        public static bool TryGetTopicFor(string funcName, out HelpBundleTopic topic)
        {
            topic = null;

            HelpTopic registryTopic;
            if (!HelpLinks.TryGetTopic(funcName, out registryTopic))
                return false;
            if (string.IsNullOrEmpty(registryTopic.Slug))
                return false;

            return TryGetTopicBySlug(registryTopic.Slug, out topic);
        }

        public static bool TryGetTopicBySlug(string slug, out HelpBundleTopic topic)
        {
            topic = null;
            if (string.IsNullOrEmpty(slug))
                return false;

            lock (Gate)
            {
                EnsureLoaded();
                return _bySlug.TryGetValue(slug, out topic);
            }
        }

        /// <summary>XHTML статьи. null, если раздела нет.</summary>
        public static string ReadArticle(HelpBundleTopic topic)
        {
            if (topic == null || string.IsNullOrEmpty(topic.File))
                return null;

            lock (Gate)
            {
                EnsureLoaded();
                if (_archive == null)
                    return null;

                ZipArchiveEntry entry = _archive.GetEntry(topic.File);
                if (entry == null)
                    return null;

                using (Stream entryStream = entry.Open())
                using (var reader = new StreamReader(entryStream, System.Text.Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        /// <summary>
        /// Байты картинки по пути из бандла (img/x.png).
        /// Копируем в массив: ZipArchiveEntry.Open() отдаёт неseekable DeflateStream,
        /// а BitmapImage.StreamSource требует seekable поток.
        /// </summary>
        public static byte[] ReadAsset(string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath))
                return null;

            lock (Gate)
            {
                EnsureLoaded();
                if (_archive == null)
                    return null;

                ZipArchiveEntry entry = _archive.GetEntry(entryPath);
                if (entry == null)
                    return null;

                var buffer = new MemoryStream((int)Math.Max(entry.Length, 0));
                using (Stream entryStream = entry.Open())
                {
                    entryStream.CopyTo(buffer);
                }

                return buffer.ToArray();
            }
        }

        private static void EnsureLoaded()
        {
            if (_initialized)
                return;

            _initialized = true;
            _bySlug = new Dictionary<string, HelpBundleTopic>(StringComparer.OrdinalIgnoreCase);
            _ordered = new List<HelpBundleTopic>();

            try
            {
                Assembly assembly = typeof(HelpBundle).Assembly;
                _stream = assembly.GetManifestResourceStream(ResourceName);
                if (_stream == null)
                    return; // бандл не вшит — панель работает как оглавление со ссылками в вики

                _archive = new ZipArchive(_stream, ZipArchiveMode.Read, false);

                ZipArchiveEntry manifestEntry = _archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                    return;

                string json;
                using (Stream manifestStream = manifestEntry.Open())
                using (var reader = new StreamReader(manifestStream, System.Text.Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                HelpBundleManifest manifest = JsonConvert.DeserializeObject<HelpBundleManifest>(json);
                if (manifest == null || manifest.Topics == null)
                    return;

                _ordered = manifest.Topics
                    .Where(t => t != null && !string.IsNullOrEmpty(t.Slug))
                    .OrderBy(t => t.Sort)
                    .ThenBy(t => t.Title, StringComparer.CurrentCulture)
                    .ToList();

                foreach (HelpBundleTopic topic in _ordered)
                    _bySlug[topic.Slug] = topic;
            }
            catch (Exception)
            {
                // Битый или отсутствующий бандл не должен ломать плагин:
                // панель просто останется оглавлением со ссылками в вики.
                Dispose();
                _bySlug = new Dictionary<string, HelpBundleTopic>(StringComparer.OrdinalIgnoreCase);
                _ordered = new List<HelpBundleTopic>();
            }
        }

        internal static void Dispose()
        {
            lock (Gate)
            {
                if (_archive != null)
                {
                    try { _archive.Dispose(); } catch (Exception) { }
                    _archive = null;
                }

                if (_stream != null)
                {
                    try { _stream.Dispose(); } catch (Exception) { }
                    _stream = null;
                }
            }
        }
    }
}
