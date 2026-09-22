using System;
using System.Globalization;
using System.IO;

namespace TNovCommon
{
    /// <summary>
    /// Диагностика подавленных исключений обновителей (IUpdater).
    ///
    /// Любое исключение, вылетевшее из IUpdater.Execute, Revit показывает пользователю
    /// с предложением отключить обновитель, поэтому обновители обязаны гасить всё у себя.
    /// Чтобы такие сбои не пропадали бесследно, они пишутся сюда.
    ///
    /// Класс сам никогда не бросает исключений и ограничивает число записей за сеанс.
    /// </summary>
    public static class UpdaterDiagnostics
    {
        const int MaxRecords = 500;

        static readonly object _lock = new object();
        static int _count;
        static string _path;
        static bool _pathResolved;

        /// <summary>Файл журнала или null, если каталог недоступен.</summary>
        public static string LogPath
        {
            get { lock (_lock) { return ResolvePath(); } }
        }

        public static void Report(string updaterName, string context, Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    if (_count >= MaxRecords) return;
                    _count++;

                    string path = ResolvePath();
                    if (path == null) return;

                    string line = string.Format(CultureInfo.InvariantCulture,
                        "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}: {3}: {4}",
                        DateTime.Now,
                        updaterName ?? "?",
                        context ?? "",
                        ex == null ? "<null>" : ex.GetType().Name,
                        ex == null ? "" : ex.Message);

                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch
            {
                // диагностика не должна мешать работе обновителя
            }
        }

        static string ResolvePath()
        {
            if (_pathResolved) return _path;
            _pathResolved = true;
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "TNovClient", "logs");
                Directory.CreateDirectory(folder);
                _path = Path.Combine(folder, "updaters-errors.log");
            }
            catch
            {
                _path = null;
            }
            return _path;
        }
    }
}
