using System;

namespace TNovCommon
{
    /// <summary>
    /// Имя модели без суффикса локальной копии «_пользователь».
    /// </summary>
    public static class ModelName
    {
        /// <summary>
        /// Отсекает «_userName» только с конца имени и без учёта регистра.
        /// При пустом userName имя не трогает: прежний Replace("_" + "", "")
        /// выкидывал все подчёркивания и плодил файлы вида «…-С2krasnopeeva.a.json».
        /// </summary>
        public static string StripUserSuffix(string docName, string userName)
        {
            if (string.IsNullOrEmpty(docName)) return docName ?? "";
            if (string.IsNullOrEmpty(userName)) return docName;

            // doc.Title бывает с «.rvt» (если в Windows включён показ расширений) — расширение сохраняем
            string ext = "";
            string name = docName;
            if (name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                ext = name.Substring(name.Length - 4);
                name = name.Substring(0, name.Length - 4);
            }

            string suffix = "_" + userName;
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - suffix.Length) + ext;
            return docName;
        }
    }
}
