using System.Net;
using System.Net.Http;
using CmlLib.Core.Auth.Microsoft;

namespace MintLauncher.Operational;

internal sealed record AuthenticationHttpFailure(string Stage, int StatusCode, bool InvalidAppRegistration);

/// <summary>
/// Keeps only the failed service and a recognized error category. Request URLs, response bodies,
/// authorization codes and tokens are never retained by diagnostics.
/// </summary>
internal sealed class MicrosoftAuthenticationDiagnosticsHandler(HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    private AuthenticationHttpFailure? _lastFailure;

    public AuthenticationHttpFailure? LastFailure => Volatile.Read(ref _lastFailure);

    public void Reset() => Interlocked.Exchange(ref _lastFailure, null);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode || !TryGetStage(request.RequestUri, out var stage))
            return response;

        var invalidAppRegistration = false;
        if (response.Content is not null)
        {
            try
            {
                // The library reads this content again. Reading it here only buffers it in memory;
                // no part of the response body is written to disk or displayed to the user.
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                invalidAppRegistration = body.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Diagnostics must never make an otherwise usable authentication response fail.
            }
        }

        Interlocked.Exchange(ref _lastFailure,
            new AuthenticationHttpFailure(stage, (int)response.StatusCode, invalidAppRegistration));
        return response;
    }

    private static bool TryGetStage(Uri? uri, out string stage)
    {
        stage = "";
        if (uri is null || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        stage = (uri.Host.ToLowerInvariant(), uri.AbsolutePath.ToLowerInvariant()) switch
        {
            ("api.minecraftservices.com", "/authentication/login_with_xbox") => "Minecraft 身份验证",
            ("api.minecraftservices.com", "/entitlements/mcstore") => "游戏所有权检查",
            ("api.minecraftservices.com", "/minecraft/profile") => "Minecraft 档案读取",
            ("user.auth.xboxlive.com", "/user/authenticate") => "Xbox 登录",
            ("xsts.auth.xboxlive.com", "/xsts/authorize") => "Xbox 身份验证",
            _ => ""
        };
        return stage.Length > 0;
    }
}

internal static class MicrosoftAuthenticationFailureMessage
{
    public static string Create(Exception exception, AuthenticationHttpFailure? failure)
    {
        var authException = FindAuthException(exception);
        var invalidAppRegistration = failure?.InvalidAppRegistration == true ||
            ContainsInvalidAppRegistration(authException);

        if (invalidAppRegistration)
            return "Minecraft 服务返回 Invalid app registration：当前应用 ID 尚未获得该服务的访问资格。需要提交应用 ID 审核；重复登录或切换设备码登录不会改变这个结果。";

        if (failure is not null)
            return $"失败阶段：{failure.Stage}；HTTP {failure.StatusCode}。服务没有返回可确认的应用注册错误，暂不能判断为应用 ID 审核问题。";

        if (authException?.StatusCode > 0)
            return $"Minecraft 验证失败：HTTP {authException.StatusCode}。未能确认具体失败接口或原因，请重试。";

        if (exception is HttpRequestException httpException && httpException.StatusCode is HttpStatusCode status)
            return $"登录网络请求失败：HTTP {(int)status}。未能确认具体失败接口或原因，请重试。";

        return $"登录未完成（{exception.GetBaseException().GetType().Name}）。请重试；不会显示可能包含登录凭据的原始响应。";
    }

    private static JEAuthException? FindAuthException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is JEAuthException authException)
                return authException;
        return null;
    }

    private static bool ContainsInvalidAppRegistration(JEAuthException? exception) =>
        exception is not null &&
        new[] { exception.Error, exception.ErrorType, exception.ErrorMessage }
            .Any(value => value?.Contains("Invalid app registration", StringComparison.OrdinalIgnoreCase) == true);
}
