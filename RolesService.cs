using System;
using System.Collections.Generic;
using TNovCommon.Server;

namespace TNovCommon
{
    /// <summary>
    /// Отдел и роль пользователя из roles.txt в корне серверной папки:
    /// «login,DEPT[,role]», строки «::» — комментарии. DEPT: BIM, AR, ST, VK, OV, EL, SS; role: head, user.
    /// Логин сравнивается точно, без учёта регистра (домен и @… отбрасываются).
    /// В личной конфигурации roles.txt нет — пользователь получает полные права (BIM, head).
    /// Отсутствие файла или записи — не ошибка: отдел и роль "-".
    /// </summary>
    public static class RolesService
    {
        public const string None = "-";

        public sealed class UserRole
        {
            public string Department { get; }
            public string Role { get; }
            /// <summary>Пользователь найден в roles.txt (или личная конфигурация).</summary>
            public bool Found => Department != None;

            public UserRole(string department, string role)
            {
                Department = department;
                Role = role;
            }

            public bool IsDepartment(string dept) => string.Equals(Department, dept, StringComparison.OrdinalIgnoreCase);
            public bool IsBim => IsDepartment("BIM");
        }

        private static readonly UserRole NotFound = new UserRole(None, None);
        private static readonly UserRole Personal = new UserRole("BIM", "head");

        /// <summary>
        /// Роль текущего пользователя: Revit-имя, учётка Windows и имя из Revit.ini.
        /// revitUserName — Application.Username (может быть null).
        /// </summary>
        public static UserRole GetCurrentUser(string revitUserName) =>
            Resolve(TNovConfigLoad.GetCachedConfig(), GetLoginCandidates(revitUserName)) ?? NotFound;

        /// <summary>Роль пользователя строго по указанному логину (без учётки Windows).</summary>
        public static UserRole GetUser(string login) => TryGetUser(login, out UserRole role) ? role : NotFound;

        /// <summary>false — roles.txt не прочитан (сервер недоступен, файла нет): судить о роли нельзя.</summary>
        public static bool TryGetUser(string login, out UserRole role)
        {
            role = Resolve(TNovConfigLoad.GetCachedConfig(), GetLoginCandidates(login, includeCurrent: false));
            return role != null;
        }

        /// <summary>null — конфигурация или roles.txt не читаются.</summary>
        private static UserRole Resolve(TNovConfig config, string[] candidates)
        {
            if (config == null) return null;
            if (!CorpGate.IsCorp(config)) return Personal;
            if (!ServerData.TryReadAllLines("roles.txt", out string[] lines)) return null;

            foreach (string raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string trimmed = raw.Trim();
                if (trimmed.StartsWith("::")) continue;

                string[] parts = trimmed.Split(',');
                if (parts.Length < 2) continue;

                string login = parts[0].Trim();
                if (login.Length == 0) continue;
                foreach (string candidate in candidates)
                {
                    if (!string.Equals(login, candidate, StringComparison.OrdinalIgnoreCase)) continue;
                    string dept = parts[1].Trim();
                    string role = parts.Length > 2 ? parts[2].Trim() : None;
                    return new UserRole(dept.Length > 0 ? dept : None, role.Length > 0 ? role : None);
                }
            }
            return NotFound;
        }

        /// <summary>Варианты логина: как есть, без домена, без @…; плюс учётка Windows и Revit.ini.</summary>
        public static string[] GetLoginCandidates(string userName, bool includeCurrent = true)
        {
            var names = new List<string>();
            AddCandidate(names, userName);
            if (includeCurrent)
            {
                AddCandidate(names, Environment.UserName);
                try { AddCandidate(names, UserNameHelper.GetCurrentUserName(true)); } catch { }
            }
            return names.ToArray();
        }

        private static void AddCandidate(List<string> names, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string name = value.Trim();
            if (!names.Contains(name)) names.Add(name);

            int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
            if (slash >= 0 && slash < name.Length - 1)
            {
                name = name.Substring(slash + 1).Trim();
                if (name.Length > 0 && !names.Contains(name)) names.Add(name);
            }

            int at = name.IndexOf('@');
            if (at > 0)
            {
                string beforeAt = name.Substring(0, at).Trim();
                if (beforeAt.Length > 0 && !names.Contains(beforeAt)) names.Add(beforeAt);
            }
        }
    }
}
