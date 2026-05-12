using System;
using System.Threading.Tasks;

namespace TNovCommon
{
    public static class AuthenticationService
    {
        /// <summary>
        /// Пытается получить текущего пользователя. Алгоритм:
        /// 1. Текущий доменный пользователь Windows (без пароля).
        /// 2. Сохранённые учётные данные из CredentialManager.
        /// 3. Окно ручного ввода (если предыдущие шаги не удались).
        /// Возвращает null, если пользователь нажал "Отмена" в окне входа.
        /// Выбрасывает исключение при ошибке аутентификации.
        /// </summary>
        public static UserInfo Authenticate(IAuthProvider authProvider)
        {
            // Шаг 1: текущий пользователь Windows
            try
            {
                return Task.Run(() => authProvider.AuthenticateCurrentUserAsync()).Result;
            }
            catch
            {
                // не удалось – идём дальше
            }

            // Шаг 2: сохранённые учётные данные
            var (upn, password) = CredentialManager.Load();
            if (!string.IsNullOrEmpty(upn) && !string.IsNullOrEmpty(password))
            {
                try
                {
                    return Task.Run(() => authProvider.AuthenticateAsync(upn, password)).Result;
                }
                catch
                {
                    // очищаем невалидные сохранённые данные
                    CredentialManager.Clear();
                }
            }

            // Шаг 3: окно ручного ввода
            var loginWindow = new LoginWindow();
            if (loginWindow.ShowDialog() != true)
                return null; // пользователь отказался от входа

            try
            {
                var user = Task.Run(() =>
                    authProvider.AuthenticateAsync(loginWindow.Upn, loginWindow.Password)).Result;
                if (loginWindow.RememberMe)
                    CredentialManager.Save(loginWindow.Upn, loginWindow.Password);
                return user;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка аутентификации: {ex.Message}", ex);
            }
        }
    }
}