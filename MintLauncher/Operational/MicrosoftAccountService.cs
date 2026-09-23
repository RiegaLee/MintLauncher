using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.Accounts.JsonStorage;
using XboxAuthNet.Game.Msal;

namespace MintLauncher.Operational;

public sealed record MicrosoftGameAccount(string Uuid, string Name);

/// <summary>
/// Microsoft OAuth, Xbox Live and Minecraft Java authentication. Passwords never pass through
/// the launcher. MSAL owns the encrypted OAuth cache; the short-lived game sessions are also
/// protected with Windows DPAPI for the current Windows user.
/// </summary>
public sealed class MicrosoftAccountService
{
    public const string ClientId = "451e2bfb-b491-43e6-a43b-5831917b7e1f";

    private static readonly string AccountDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MintLauncher");

    private readonly MicrosoftAuthenticationDiagnosticsHandler _diagnostics = new(new HttpClientHandler());
    private readonly JELoginHandler _loginHandler;
    private Task<Microsoft.Identity.Client.IPublicClientApplication>? _msalApplication;

    public MicrosoftAccountService()
    {
        var accountStorage = new JsonXboxGameAccountManager(
            new ProtectedAccountStorage(Path.Combine(AccountDirectory, "microsoft-accounts.dat")),
            JEGameAccount.FromSessionStorage,
            JsonXboxGameAccountManager.DefaultSerializerOption);
        _loginHandler = new JELoginHandlerBuilder()
            .WithAccountManager(accountStorage)
            .WithHttpClient(new HttpClient(_diagnostics))
            .Build();
    }

    public IReadOnlyList<MicrosoftGameAccount> GetAccounts() => _loginHandler.AccountManager.GetAccounts()
        .OfType<JEGameAccount>()
        .Where(account => !string.IsNullOrWhiteSpace(account.Profile?.UUID) &&
                          !string.IsNullOrWhiteSpace(account.Profile?.Username))
        .Select(account => new MicrosoftGameAccount(account.Profile!.UUID!, account.Profile.Username!))
        .ToArray();

    public async Task<MicrosoftGameAccount> SignInAsync(CancellationToken cancellationToken = default)
    {
        _diagnostics.Reset();
        var app = await GetMsalApplicationAsync();
        var authenticator = _loginHandler.CreateAuthenticatorWithNewAccount(cancellationToken);
        var cachedResult = await TryGetCachedMicrosoftTokenAsync(app, cancellationToken);
        if (cachedResult is null)
            authenticator.AddMsalOAuth(app, oauth => oauth.SystemBrowser());
        else
            authenticator.AddMsalOAuth(app, oauth => oauth.FromResult(cachedResult));
        authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddForceJEAuthenticator();
        var session = await authenticator.ExecuteForLauncherAsync();
        return ToAccount(session);
    }

    public string DescribeSignInFailure(Exception exception) =>
        MicrosoftAuthenticationFailureMessage.Create(exception, _diagnostics.LastFailure);

    private async Task<Microsoft.Identity.Client.AuthenticationResult?> TryGetCachedMicrosoftTokenAsync(
        Microsoft.Identity.Client.IPublicClientApplication app, CancellationToken cancellationToken)
    {
        // Never silently select an account while the user is explicitly adding another one.
        if (GetAccounts().Count > 0)
            return null;

        var cachedAccounts = (await app.GetAccountsAsync()).ToArray();
        if (cachedAccounts.Length != 1)
            return null;

        try
        {
            return await app.AcquireTokenSilent(MsalClientHelper.XboxScopes, cachedAccounts[0])
                .ExecuteAsync(cancellationToken);
        }
        catch (Microsoft.Identity.Client.MsalUiRequiredException)
        {
            return null;
        }
    }

    public async Task<MSession> GetLaunchSessionAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var account = _loginHandler.AccountManager.GetAccounts()
            .OfType<JEGameAccount>()
            .FirstOrDefault(value => string.Equals(value.Profile?.UUID, uuid, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("找不到已保存的微软账户，请重新登录。");
        var app = await GetMsalApplicationAsync();
        var authenticator = _loginHandler.CreateAuthenticator(account, cancellationToken);
        authenticator.AddMsalOAuth(app, oauth => oauth.Silent());
        authenticator.AddXboxAuthForJE(xbox => xbox.Basic());
        authenticator.AddJEAuthenticator();
        try
        {
            var session = await authenticator.ExecuteForLauncherAsync();
            if (string.IsNullOrWhiteSpace(session.AccessToken) || string.IsNullOrWhiteSpace(session.UUID))
                throw new InvalidOperationException("微软账户没有返回可用于启动游戏的身份，请重新登录。");
            return session;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException("微软账户验证已过期或暂时不可用，请在账户菜单中重新登录。", exception);
        }
    }

    private async Task<Microsoft.Identity.Client.IPublicClientApplication> GetMsalApplicationAsync()
    {
        _msalApplication ??= MsalClientHelper.BuildApplicationWithCache(ClientId, new MsalCacheSettings
        {
            CacheDir = AccountDirectory,
            CacheFileName = "microsoft-oauth-cache.dat"
        });
        return await _msalApplication;
    }

    private static MicrosoftGameAccount ToAccount(MSession session)
    {
        if (string.IsNullOrWhiteSpace(session.UUID) || string.IsNullOrWhiteSpace(session.Username) ||
            string.IsNullOrWhiteSpace(session.AccessToken))
            throw new InvalidOperationException("此微软账户没有可用的 Minecraft Java 版档案；请确认已拥有游戏。");
        return new MicrosoftGameAccount(session.UUID, session.Username);
    }

    private sealed class ProtectedAccountStorage(string filePath) : IJsonStorage
    {
        public JsonNode? ReadAsJsonNode()
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                var encrypted = File.ReadAllBytes(filePath);
                var plaintext = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                try { return JsonNode.Parse(plaintext); }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            catch (Exception exception) when (exception is CryptographicException or JsonException)
            {
                // A damaged or different user's cache is not a reason to make the launcher unusable.
                return null;
            }
        }

        public void Write(JsonNode node, JsonSerializerOptions? serializerOptions)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(node, serializerOptions);
            try
            {
                var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
                var temporary = filePath + ".tmp";
                File.WriteAllBytes(temporary, encrypted);
                File.Move(temporary, filePath, true);
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
    }
}
