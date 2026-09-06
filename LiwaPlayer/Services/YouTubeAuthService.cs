using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LiwaPlayer.Services
{
    // YouTube oturum çerezlerini Windows DPAPI ile şifreleyip diskte saklar.
    // Şifreleme kullanıcı hesabına bağlıdır; dosya başka makinede/kullanıcıda açılamaz.
    public class YouTubeAuthService
    {
        private class CookieDto
        {
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
            public string Domain { get; set; } = "";
            public string Path { get; set; } = "/";
        }

        private readonly string _authFile;

        public IReadOnlyList<Cookie>? Cookies { get; private set; }

        public bool IsLoggedIn => Cookies is { Count: > 0 };

        public YouTubeAuthService()
        {
            _authFile = Path.Combine(AppPaths.DataFolder, "auth.bin");

            Load();
        }

        public void SaveCookies(IEnumerable<Cookie> cookies)
        {
            var list = cookies
                .Select(c => new CookieDto
                {
                    Name = c.Name,
                    Value = c.Value,
                    Domain = c.Domain,
                    Path = c.Path
                })
                .ToList();

            try
            {
                var json = JsonSerializer.Serialize(list);

                var encrypted = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(json),
                    null,
                    DataProtectionScope.CurrentUser);

                File.WriteAllBytes(_authFile, encrypted);
            }
            catch
            {
                // Kaydedilemese bile oturum bu çalıştırma boyunca bellekte geçerli kalır
            }

            Cookies = list
                .Select(d => new Cookie(d.Name, d.Value, d.Path, d.Domain))
                .ToList();
        }

        public void Logout()
        {
            Cookies = null;

            try
            {
                if (File.Exists(_authFile))
                    File.Delete(_authFile);
            }
            catch
            {
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_authFile))
                    return;

                var encrypted = File.ReadAllBytes(_authFile);

                var json = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser));

                var list = JsonSerializer.Deserialize<List<CookieDto>>(json);

                if (list is { Count: > 0 })
                    Cookies = list
                        .Select(d => new Cookie(d.Name, d.Value, d.Path, d.Domain))
                        .ToList();
            }
            catch
            {
                // Bozuk/çözülemeyen dosya: oturumsuz devam et
                Cookies = null;
            }
        }
    }
}
