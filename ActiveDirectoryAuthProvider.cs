using System;
using System.DirectoryServices.AccountManagement;
using System.Security;
using System.Security.Principal;
using System.Threading.Tasks;

namespace TNovCommon
{
    public class SimpleAuthProvider : IAuthProvider
    {
        public Task<UserInfo> AuthenticateAsync(string upn, string password)
        {
            return Task.FromResult(new UserInfo
            {
                Upn = upn,
                DisplayName = upn  // или любое фиксированное имя
            });
        }
        public Task<UserInfo> AuthenticateCurrentUserAsync()
        {
            // Возвращаем данные текущего пользователя Windows
            string upn = WindowsIdentity.GetCurrent().Name; // или сформировать UPN
            return Task.FromResult(new UserInfo { Upn = upn, DisplayName = upn });
        }
    }
    public class ActiveDirectoryAuthProvider : IAuthProvider
    {
        private readonly string _domainName;
        private readonly string _ldapServer;

        public ActiveDirectoryAuthProvider(string domainName, string ldapServer = null)
        {
            _domainName = domainName ?? throw new ArgumentNullException(nameof(domainName));
            _ldapServer = ldapServer;
        }

        public Task<UserInfo> AuthenticateAsync(string upn, string password)
        {
            return Task.Run(() =>
            {
                string containerName = _domainName;
                ContextType contextType = ContextType.Domain;
                ContextOptions options = ContextOptions.Negotiate;

                if (!string.IsNullOrEmpty(_ldapServer))
                {
                    containerName = $"{_ldapServer}:636";
                    options |= ContextOptions.SecureSocketLayer;
                }

                using (var ctx = new PrincipalContext(contextType, containerName, null, options))
                {
                    bool valid = ctx.ValidateCredentials(upn, password);
                    if (!valid)
                        throw new SecurityException("Неверное имя пользователя или пароль.");

                    using (var userPrincipal = UserPrincipal.FindByIdentity(ctx, IdentityType.UserPrincipalName, upn))
                    {
                        return new UserInfo
                        {
                            Upn = upn,
                            DisplayName = userPrincipal?.DisplayName ?? upn
                        };
                    }
                }
            });
        }

        public Task<UserInfo> AuthenticateCurrentUserAsync()
        {
            return Task.Run(() =>
            {
                using (var userPrincipal = UserPrincipal.Current)
                {
                    if (userPrincipal == null)
                        throw new SecurityException("Не удалось получить текущего пользователя домена. Убедитесь, что машина в домене.");

                    return new UserInfo
                    {
                        Upn = userPrincipal.UserPrincipalName,
                        DisplayName = userPrincipal.DisplayName ?? userPrincipal.SamAccountName
                    };
                }
            });
        }
    }
}
