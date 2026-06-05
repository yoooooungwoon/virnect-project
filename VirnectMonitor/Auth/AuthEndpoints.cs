using System.Net;
using System.Text.Json;

namespace VirnectMonitor.Auth;

public static class AuthEndpoints
{
    private const string AuthThemeStyles = """
                :root {
                  --primary: #273096;
                  --button: #6654ad;
                  --text: #151733;
                  --muted: #5c6080;
                  --line: rgba(39, 48, 150, 0.22);
                  --danger: #b42318;
                  --danger-bg: #fff5f5;
                  --surface: #ffffff;
                }

                * { box-sizing: border-box; }

                html {
                  min-height: 100%;
                  background: var(--surface);
                }

                body {
                  min-height: 100vh;
                  min-height: 100dvh;
                  margin: 0;
                  background: var(--surface);
                  color: var(--text);
                  font-family: "Segoe UI", "Malgun Gothic", Arial, sans-serif;
                  letter-spacing: 0;
                  overflow-x: hidden;
                }

                .page-frame {
                  display: flex;
                  justify-content: center;
                  align-items: flex-start;
                  width: auto;
                  max-width: 100vw;
                  min-height: 100vh;
                  min-height: 100dvh;
                  box-sizing: border-box;
                  border: 2px solid var(--primary);
                  border-radius: 28px;
                  background: var(--surface);
                  overflow-x: hidden;
                  padding: 96px 124px;
                }

                .auth-panel {
                  width: 100%;
                  min-width: 0;
                  max-width: 884px;
                }

                .auth-title {
                  margin: 0 0 92px;
                  color: var(--primary);
                  font-size: 44px;
                  font-weight: 800;
                  line-height: 1.15;
                }

                .auth-form {
                  width: 100%;
                  min-width: 0;
                }

                .field-stack {
                  display: grid;
                  gap: 68px;
                  min-width: 0;
                }

                .form-field {
                  display: grid;
                  gap: 8px;
                  min-width: 0;
                }

                .form-field label {
                  color: var(--primary);
                  font-size: 26px;
                  font-weight: 800;
                  line-height: 1.2;
                }

                .form-field input {
                  width: 100%;
                  max-width: 100%;
                  box-sizing: border-box;
                  height: 78px;
                  border: 1.5px solid var(--primary);
                  border-radius: 6px;
                  background: #fff;
                  color: var(--text);
                  font-size: 24px;
                  padding: 0 20px;
                  outline: none;
                }

                .form-field input:focus {
                  box-shadow: 0 0 0 3px rgba(39, 48, 150, 0.14);
                }

                .auth-button {
                  width: 100%;
                  max-width: 100%;
                  box-sizing: border-box;
                  min-height: 82px;
                  margin-top: 92px;
                  border: 1px solid var(--primary);
                  border-radius: 8px;
                  background: var(--button);
                  color: #fff;
                  font-size: 34px;
                  font-weight: 800;
                  line-height: 1.2;
                  cursor: pointer;
                }

                .auth-button:hover,
                .auth-button:focus-visible,
                .button:hover,
                .button:focus-visible {
                  background: var(--primary);
                }

                .form-error {
                  margin: -36px 0 36px;
                  padding: 14px 18px;
                  border: 1px solid #e19393;
                  border-radius: 6px;
                  background: var(--danger-bg);
                  color: var(--danger);
                  font-size: 16px;
                  font-weight: 700;
                }

                .setup-panel .auth-title {
                  margin-bottom: 56px;
                }

                .setup-panel .field-stack {
                  gap: 34px;
                }

                .setup-panel .auth-button {
                  margin-top: 54px;
                }

                .result-panel {
                  max-width: 820px;
                }

                .result-panel .auth-title {
                  margin-bottom: 24px;
                }

                .status-message {
                  max-width: 720px;
                  margin: 0 0 40px;
                  color: var(--muted);
                  font-size: 20px;
                  line-height: 1.5;
                }

                .status-grid {
                  display: grid;
                  grid-template-columns: 170px minmax(0, 1fr);
                  max-width: 660px;
                  margin: 0;
                  border-top: 1px solid var(--line);
                }

                .status-grid dt,
                .status-grid dd {
                  margin: 0;
                  padding: 18px 0;
                  border-bottom: 1px solid var(--line);
                  font-size: 18px;
                }

                .status-grid dt {
                  color: var(--primary);
                  font-weight: 800;
                }

                .status-grid dd {
                  color: var(--text);
                  font-variant-numeric: tabular-nums;
                }

                .timer {
                  color: var(--primary);
                  font-size: 42px;
                  font-weight: 800;
                  line-height: 1;
                }

                .actions {
                  min-height: 50px;
                  margin-top: 40px;
                }

                .button {
                  display: inline-flex;
                  align-items: center;
                  justify-content: center;
                  min-height: 50px;
                  padding: 0 24px;
                  border: 1px solid var(--primary);
                  border-radius: 8px;
                  background: var(--button);
                  color: #fff;
                  font-family: inherit;
                  font-size: 18px;
                  font-weight: 800;
                  text-decoration: none;
                  cursor: pointer;
                }

                .monitor-body {
                  min-height: 100vh;
                  min-height: 100dvh;
                  margin: 0;
                  background: #fff;
                  color: var(--text);
                }

                .monitor-frame {
                  min-height: 100vh;
                  min-height: 100dvh;
                  border: 2px solid var(--primary);
                  border-radius: 28px;
                  padding: 44px;
                  background: #fff;
                }

                .monitor-header {
                  display: flex;
                  align-items: flex-end;
                  justify-content: space-between;
                  gap: 24px;
                  margin-bottom: 28px;
                  padding-bottom: 24px;
                  border-bottom: 1px solid var(--line);
                }

                .monitor-title {
                  margin: 0;
                  color: var(--primary);
                  font-size: 36px;
                  font-weight: 800;
                  line-height: 1.2;
                }

                .monitor-meta {
                  margin: 0;
                  color: var(--muted);
                  font-size: 15px;
                  font-weight: 700;
                }

                .monitor-section {
                  margin-top: 34px;
                }

                .monitor-section h2 {
                  margin: 0 0 14px;
                  color: var(--primary);
                  font-size: 22px;
                  font-weight: 800;
                }

                .table-wrap {
                  overflow-x: auto;
                  border: 1px solid var(--line);
                  border-radius: 8px;
                }

                table {
                  width: 100%;
                  min-width: 900px;
                  border-collapse: collapse;
                }

                th,
                td {
                  padding: 12px 14px;
                  border-bottom: 1px solid var(--line);
                  text-align: left;
                  font-size: 14px;
                  white-space: nowrap;
                }

                th {
                  background: var(--primary);
                  color: #fff;
                  font-weight: 800;
                }

                tr:last-child td {
                  border-bottom: 0;
                }

                .status-cell {
                  color: var(--primary);
                  font-weight: 800;
                }

                @media (max-width: 760px) {
                  .page-frame {
                    border-radius: 20px;
                    padding: 44px 28px;
                  }

                  .auth-title {
                    margin-bottom: 54px;
                    font-size: 34px;
                  }

                  .field-stack {
                    gap: 36px;
                  }

                  .form-field label {
                    font-size: 22px;
                  }

                  .form-field input {
                    height: 62px;
                    font-size: 20px;
                  }

                  .auth-button {
                    min-height: 66px;
                    margin-top: 56px;
                    font-size: 26px;
                  }

                  .status-grid {
                    grid-template-columns: 1fr;
                  }

                  .status-grid dt {
                    padding-bottom: 4px;
                    border-bottom: 0;
                  }

                  .status-grid dd {
                    padding-top: 0;
                  }

                  .monitor-frame {
                    border-radius: 20px;
                    padding: 28px;
                  }

                  .monitor-header {
                    display: block;
                  }

                  .monitor-title {
                    font-size: 30px;
                  }

                  .monitor-meta {
                    margin-top: 10px;
                  }
                }

                @media (max-width: 480px) {
                  .page-frame {
                    padding: 32px 20px;
                  }

                  .auth-title {
                    font-size: 30px;
                  }

                  .timer {
                    font-size: 34px;
                  }
                }

                @media (max-width: 430px) {
                  .page-frame {
                    width: 100vw;
                    max-width: 100vw;
                    border-radius: 18px;
                    padding: 38px 22px;
                  }

                  .auth-panel {
                    width: calc(100vw - 48px);
                    max-width: calc(100vw - 48px);
                  }

                  .auth-title {
                    margin-bottom: 46px;
                    font-size: 32px;
                  }

                  .field-stack {
                    gap: 34px;
                  }

                  .form-field {
                    gap: 7px;
                  }

                  .form-field label {
                    font-size: 21px;
                  }

                  .form-field input {
                    height: 60px;
                    padding: 0 14px;
                    font-size: 19px;
                  }

                  .auth-button {
                    min-height: 64px;
                    margin-top: 48px;
                    font-size: 26px;
                  }

                  .setup-panel .auth-title {
                    margin-bottom: 38px;
                  }

                  .setup-panel .field-stack {
                    gap: 24px;
                  }

                  .setup-panel .auth-button {
                    margin-top: 36px;
                  }

                  .result-panel .auth-title {
                    margin-bottom: 18px;
                  }

                  .status-message {
                    margin-bottom: 24px;
                    font-size: 16px;
                  }

                  .status-grid dt,
                  .status-grid dd {
                    padding: 12px 0;
                    font-size: 16px;
                  }

                  .actions {
                    min-height: 46px;
                    margin-top: 28px;
                  }

                  .button {
                    min-height: 46px;
                    padding: 0 18px;
                    font-size: 16px;
                  }

                  .monitor-frame {
                    border-radius: 18px;
                    padding: 20px 16px;
                  }

                  .monitor-title {
                    font-size: 28px;
                  }

                  .monitor-section {
                    margin-top: 26px;
                  }

                  .monitor-section h2 {
                    font-size: 20px;
                  }

                  th,
                  td {
                    padding: 10px 12px;
                    font-size: 13px;
                  }
                }

                @media (max-width: 360px) {
                  .page-frame {
                    border-radius: 16px;
                    padding: 28px 16px;
                  }

                  .auth-panel {
                    width: calc(100vw - 36px);
                    max-width: calc(100vw - 36px);
                  }

                  .auth-title {
                    margin-bottom: 36px;
                    font-size: 28px;
                  }

                  .field-stack {
                    gap: 28px;
                  }

                  .form-field label {
                    font-size: 19px;
                  }

                  .form-field input {
                    height: 56px;
                    font-size: 18px;
                  }

                  .auth-button {
                    min-height: 60px;
                    margin-top: 40px;
                    font-size: 22px;
                  }

                  .setup-panel .auth-title {
                    margin-bottom: 28px;
                  }

                  .setup-panel .field-stack {
                    gap: 18px;
                  }

                  .setup-panel .auth-button {
                    margin-top: 28px;
                  }

                  .timer {
                    font-size: 30px;
                  }
                }

                @media (max-height: 700px) and (max-width: 760px) {
                  .page-frame {
                    padding-top: 28px;
                    padding-bottom: 28px;
                  }

                  .auth-title {
                    margin-bottom: 34px;
                  }

                  .field-stack {
                    gap: 26px;
                  }

                  .auth-button {
                    margin-top: 36px;
                  }

                  .setup-panel .auth-title {
                    margin-bottom: 26px;
                  }

                  .setup-panel .field-stack {
                    gap: 18px;
                  }

                  .setup-panel .auth-button {
                    margin-top: 26px;
                  }
                }

                @media (orientation: landscape) and (max-height: 520px) {
                  .page-frame {
                    width: 100vw;
                    max-width: 100vw;
                    border-radius: 16px;
                    padding: 20px 28px;
                  }

                  .auth-panel {
                    width: calc(100vw - 60px);
                    max-width: 760px;
                  }

                  .auth-title {
                    margin-bottom: 20px;
                    font-size: 26px;
                  }

                  .field-stack {
                    gap: 16px;
                  }

                  .form-field label {
                    font-size: 18px;
                  }

                  .form-field input {
                    height: 48px;
                    font-size: 17px;
                  }

                  .auth-button {
                    min-height: 52px;
                    margin-top: 24px;
                    font-size: 21px;
                  }

                  .setup-panel .auth-title {
                    margin-bottom: 18px;
                  }

                  .setup-panel .field-stack {
                    gap: 12px;
                  }

                  .setup-panel .auth-button {
                    margin-top: 18px;
                  }

                  .monitor-frame {
                    border-radius: 16px;
                    padding: 20px;
                  }

                  .monitor-header {
                    margin-bottom: 18px;
                    padding-bottom: 16px;
                  }

                  .monitor-section {
                    margin-top: 22px;
                  }
                }
        """;

    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/", StartEntryAsync);
        app.MapGet("/login/start", StartEntryAsync);
        app.MapGet("/auth/start", StartEntryAsync);

        app.MapPost("/auth/start", async (AuthService auth, HttpRequest request) =>
        {
            if (await auth.IsSetupRequiredAsync())
            {
                return Results.Conflict(await auth.GetSetupStatusAsync());
            }

            return Results.Ok(await auth.StartAsync(request));
        });

        app.MapGet("/setup", async (AuthService auth) =>
        {
            if (!await auth.IsSetupRequiredAsync())
            {
                return Results.Redirect("/");
            }

            return Results.Content(RenderSetupPage(), "text/html; charset=utf-8");
        });

        app.MapPost("/setup", async (AuthService auth, HttpRequest request) =>
        {
            var setup = await ReadSetupRequestAsync(request);
            try
            {
                var response = await auth.CreateInitialAdminAsync(setup);
                if (request.HasFormContentType)
                {
                    return Results.Redirect("/auth/start");
                }

                return Results.Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                if (request.HasFormContentType)
                {
                    return Results.Content(RenderSetupPage(ex.Message), "text/html; charset=utf-8");
                }

                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/login", async (AuthService auth, string token) =>
        {
            if (!await auth.CanUseLoginTokenAsync(token))
            {
                return Results.Redirect("/auth/start");
            }

            return Results.Content(RenderLoginPage(token), "text/html; charset=utf-8");
        });

        app.MapGet("/login/result", async (AuthService auth, string token) =>
        {
            var response = await auth.CurrentAsync(token);
            return Results.Content(RenderLoginResultPage(response, token), "text/html; charset=utf-8");
        });

        app.MapPost("/auth/login", async (AuthService auth, HttpRequest request) =>
        {
            var login = await ReadLoginRequestAsync(request);
            var response = await auth.LoginAsync(login, ReadLoginAttemptMetadata(request));

            if (request.HasFormContentType)
            {
                return Results.Redirect($"/login/result?token={Uri.EscapeDataString(login.Token)}");
            }

            return Results.Ok(response);
        });

        app.MapGet("/auth/status/{token}", async (AuthService auth, string token) =>
        {
            return Results.Ok(await auth.CurrentAsync(token));
        });

        app.MapGet("/auth/current", async (AuthService auth, string? token) =>
        {
            return Results.Ok(await auth.CurrentAsync(token));
        });

        app.MapGet("/auth/current-once", async (AuthService auth, string? token) =>
        {
            return Results.Ok(await auth.CurrentOnceAsync(token));
        });

        app.MapGet("/auth/sessions", async (AuthService auth, int? limit) =>
        {
            if (!await auth.HasActiveApprovedSessionAsync())
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await auth.ListSessionsAsync(limit ?? 20));
        });

        app.MapGet("/auth/login-audits", async (AuthService auth, int? limit) =>
        {
            if (!await auth.HasActiveApprovedSessionAsync())
            {
                return Results.Unauthorized();
            }

            return Results.Ok(await auth.ListLoginAuditsAsync(limit ?? 20));
        });

        app.MapGet("/admin", async (AuthService auth) =>
        {
            if (!await auth.HasActiveApprovedSessionAsync())
            {
                return Results.Redirect("/auth/start");
            }

            var sessions = await auth.ListSessionsAsync(20);
            var audits = await auth.ListLoginAuditsAsync(20);
            return Results.Content(RenderMonitorPage(sessions, audits), "text/html; charset=utf-8");
        });

        return app;
    }

    private static async Task<IResult> StartEntryAsync(AuthService auth, HttpRequest request)
    {
        if (await auth.IsSetupRequiredAsync())
        {
            return Results.Redirect("/setup");
        }

        var start = await auth.StartAsync(request);
        return Results.Redirect(start.LoginUrl);
    }

    private static async Task<LoginRequest> ReadLoginRequestAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            return new LoginRequest(
                form["token"].ToString(),
                form["username"].ToString(),
                form["password"].ToString());
        }

        var login = await request.ReadFromJsonAsync<LoginRequest>();
        if (login is null)
        {
            throw new BadHttpRequestException("Login request body is required.");
        }

        return login;
    }

    private static LoginAttemptMetadata ReadLoginAttemptMetadata(HttpRequest request)
    {
        var forwardedFor = request.Headers["X-Forwarded-For"].ToString();
        var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
            ? forwardedFor.Split(',')[0].Trim()
            : request.HttpContext.Connection.RemoteIpAddress?.ToString();

        return new LoginAttemptMetadata(
            clientIp,
            request.Headers.UserAgent.ToString());
    }

    private static async Task<SetupAdminRequest> ReadSetupRequestAsync(HttpRequest request)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            return new SetupAdminRequest(
                form["username"].ToString(),
                form["password"].ToString(),
                form["confirmPassword"].ToString());
        }

        var setup = await request.ReadFromJsonAsync<SetupAdminRequest>();
        if (setup is null)
        {
            throw new BadHttpRequestException("Setup request body is required.");
        }

        return setup;
    }

    private static string RenderSetupPage(string? error = null)
    {
        var errorBlock = string.IsNullOrWhiteSpace(error)
            ? ""
            : $$"""<p class="form-error" role="alert">{{WebUtility.HtmlEncode(error)}}</p>""";

        return $$"""
            <!doctype html>
            <html lang="ko">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>VIRNECT Initial Admin</title>
              <style>
                {{AuthThemeStyles}}
              </style>
            </head>
            <body>
              <main class="page-frame">
                <section class="auth-panel setup-panel">
                <h1 class="auth-title">관리자 등록</h1>
                {{errorBlock}}
                <form class="auth-form" method="post" action="/setup">
                  <div class="field-stack">
                    <div class="form-field">
                      <label for="username">관리자 ID</label>
                      <input id="username" name="username" autocomplete="username" required autofocus>
                    </div>
                    <div class="form-field">
                      <label for="password">비밀번호</label>
                      <input id="password" name="password" type="password" autocomplete="new-password" minlength="8" required>
                    </div>
                    <div class="form-field">
                      <label for="confirmPassword">비밀번호 확인</label>
                      <input id="confirmPassword" name="confirmPassword" type="password" autocomplete="new-password" minlength="8" required>
                    </div>
                  </div>
                  <button class="auth-button" type="submit">등록 하기</button>
                </form>
                </section>
              </main>
            </body>
            </html>
            """;
    }

    private static string RenderLoginPage(string token)
    {
        var encodedToken = WebUtility.HtmlEncode(token);
        return $$"""
            <!doctype html>
            <html lang="ko">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>VIRNECT Login</title>
              <style>
                {{AuthThemeStyles}}
              </style>
            </head>
            <body>
              <main class="page-frame">
                <section class="auth-panel">
                <h1 class="auth-title">사용자 인증</h1>
                <form class="auth-form" method="post" action="/auth/login">
                  <input type="hidden" name="token" value="{{encodedToken}}">
                  <div class="field-stack">
                    <div class="form-field">
                      <label for="username">이름</label>
                      <input id="username" name="username" autocomplete="username" required autofocus>
                    </div>
                    <div class="form-field">
                      <label for="password">비밀번호</label>
                      <input id="password" name="password" type="password" autocomplete="current-password" required>
                    </div>
                  </div>
                  <button class="auth-button" type="submit">인증 하기</button>
                </form>
                </section>
              </main>
            </body>
            </html>
            """;
    }

    private static string RenderLoginResultPage(AuthStatusResponse response, string token)
    {
        var title = response.Authenticated ? "인증 완료" : "인증 불가";
        var message = response.Authenticated
            ? "인증이 완료되었습니다. 창을 닫고 기존 화면으로 돌아갑니다."
            : $"상태: {WebUtility.HtmlEncode(response.Status)}";
        var authExpiresIso = response.AuthExpiresAt?.ToString("O") ?? "";
        var authExpiresDisplay = response.AuthExpiresAt is null
            ? "-"
            : response.AuthExpiresAt.Value.ToString("yyyy-MM-dd HH:mm:ss 'KST'");
        var remainingDisplay = response.Authenticated && response.AuthExpiresAt is not null ? "calculating..." : "-";
        var actionLink = response.Authenticated
            ? """<button class="button" id="close-window-button" type="button">창 닫기</button>"""
            : """<a class="button" href="/auth/start">다시 로그인</a>""";
        var tokenJson = JsonSerializer.Serialize(token);

        return $$"""
            <!doctype html>
            <html lang="ko">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>VIRNECT Login Result</title>
              <style>
                {{AuthThemeStyles}}
              </style>
            </head>
            <body>
              <main class="page-frame">
                <section class="auth-panel result-panel">
                <h1 id="result-title" class="auth-title">{{WebUtility.HtmlEncode(title)}}</h1>
                <p id="result-message" class="status-message">{{message}}</p>
                <dl class="status-grid">
                  <dt>상태</dt>
                  <dd id="status">{{WebUtility.HtmlEncode(response.Status)}}</dd>
                  <dt>값</dt>
                  <dd id="value">{{response.Value}}</dd>
                  <dt>만료</dt>
                  <dd id="expires">{{authExpiresDisplay}}</dd>
                  <dt>남은 시간</dt>
                  <dd id="remaining" class="timer">{{remainingDisplay}}</dd>
                </dl>
                <div id="actions" class="actions">{{actionLink}}</div>
                </section>
              </main>
              <script>
                const token = {{tokenJson}};
                let expiresAt = "{{authExpiresIso}}";
                let authenticated = {{response.Authenticated.ToString().ToLowerInvariant()}};
                const remaining = document.getElementById("remaining");
                const statusEl = document.getElementById("status");
                const valueEl = document.getElementById("value");
                const expiresEl = document.getElementById("expires");
                const titleEl = document.getElementById("result-title");
                const messageEl = document.getElementById("result-message");
                const actionsEl = document.getElementById("actions");
                let closeAttempted = false;
                function renderRemaining() {
                  if (!authenticated || !expiresAt || !remaining) {
                    if (remaining) {
                      remaining.textContent = "-";
                    }
                    return;
                  }
                  const ms = Date.parse(expiresAt) - Date.now();
                  if (Number.isNaN(ms) || ms <= 0) {
                    remaining.textContent = "00:00";
                    return;
                  }
                  const totalSeconds = Math.ceil(ms / 1000);
                  const minutes = String(Math.floor(totalSeconds / 60)).padStart(2, "0");
                  const seconds = String(totalSeconds % 60).padStart(2, "0");
                  remaining.textContent = `${minutes}:${seconds}`;
                }
                function renderInactive(data) {
                  authenticated = false;
                  expiresAt = data.authExpiresAt || "";
                  titleEl.textContent = "인증 불가";
                  messageEl.textContent = `상태: ${data.status}`;
                  statusEl.textContent = data.status;
                  valueEl.textContent = String(data.value);
                  expiresEl.textContent = data.authExpiresAt ? new Date(data.authExpiresAt).toLocaleString("ko-KR", { timeZone: "Asia/Seoul", hour12: false }) + " KST" : "-";
                  remaining.textContent = "-";
                  actionsEl.innerHTML = '<a class="button" href="/auth/start">다시 로그인</a>';
                }
                function requestCloseWindow() {
                  closeAttempted = true;
                  window.close();
                  setTimeout(() => {
                    if (!document.hidden) {
                      messageEl.textContent = "브라우저 정책으로 자동 종료가 막히면 이 창만 닫아주세요.";
                    }
                  }, 800);
                }
                function armCloseWindow() {
                  if (!authenticated) {
                    return;
                  }

                  const closeButton = document.getElementById("close-window-button");
                  if (closeButton) {
                    closeButton.addEventListener("click", requestCloseWindow);
                  }

                  setTimeout(() => {
                    if (!closeAttempted) {
                      requestCloseWindow();
                    }
                  }, 600);
                }
                async function pollStatus() {
                  if (!token) {
                    return;
                  }
                  try {
                    const response = await fetch(`/auth/current-once?token=${encodeURIComponent(token)}`, { cache: "no-store" });
                    if (!response.ok) {
                      return;
                    }
                    const data = await response.json();
                    statusEl.textContent = data.status;
                    valueEl.textContent = String(data.value);
                    if (!data.authenticated || data.value <= 0 || data.status === "revoked" || data.status === "expired") {
                      renderInactive(data);
                    }
                  } catch {
                  }
                }
                renderRemaining();
                armCloseWindow();
                setInterval(renderRemaining, 1000);
                setInterval(pollStatus, 2000);
              </script>
            </body>
            </html>
            """;
    }

    private static string RenderMonitorPage(
        IReadOnlyList<AuthSessionView> sessions,
        IReadOnlyList<LoginAuditView> audits)
    {
        var rows = string.Join(
            Environment.NewLine,
            sessions.Select(session => $$"""
                <tr>
                  <td>{{session.Id}}</td>
                  <td class="status-cell">{{WebUtility.HtmlEncode(session.Status)}}</td>
                  <td>{{session.TransitionConsumed}}</td>
                  <td>{{WebUtility.HtmlEncode(session.Username ?? "")}}</td>
                  <td>{{session.FailureCount}}</td>
                  <td>{{FormatKoreaTime(session.CreatedAt)}}</td>
                  <td>{{FormatKoreaTime(session.AuthExpiresAt)}}</td>
                </tr>
                """));
        var auditRows = string.Join(
            Environment.NewLine,
            audits.Select(audit => $$"""
                <tr>
                  <td>{{audit.Id}}</td>
                  <td>{{FormatKoreaTime(audit.OccurredAt)}}</td>
                  <td>{{WebUtility.HtmlEncode(audit.Username ?? "")}}</td>
                  <td class="status-cell">{{WebUtility.HtmlEncode(audit.Result)}}</td>
                  <td>{{WebUtility.HtmlEncode(audit.Reason ?? "")}}</td>
                  <td>{{audit.SessionId?.ToString() ?? ""}}</td>
                  <td>{{WebUtility.HtmlEncode(audit.ClientIp ?? "")}}</td>
                </tr>
                """));

        return $$"""
            <!doctype html>
            <html lang="ko">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>VIRNECT Auth Monitor</title>
              <style>
                {{AuthThemeStyles}}
              </style>
            </head>
            <body class="monitor-body">
              <main class="monitor-frame">
                <header class="monitor-header">
                  <h1 class="monitor-title">인증 모니터</h1>
                  <p class="monitor-meta">VIRNECT Auth Monitor</p>
                </header>
                <section class="monitor-section">
                  <h2>세션</h2>
                  <div class="table-wrap">
                    <table>
                      <thead>
                        <tr>
                          <th>ID</th>
                          <th>상태</th>
                          <th>Consumed</th>
                          <th>사용자</th>
                          <th>실패</th>
                          <th>생성</th>
                          <th>인증 만료</th>
                        </tr>
                      </thead>
                      <tbody>
                        {{rows}}
                      </tbody>
                    </table>
                  </div>
                </section>
                <section class="monitor-section">
                  <h2>로그인 시도</h2>
                  <div class="table-wrap">
                    <table>
                      <thead>
                        <tr>
                          <th>ID</th>
                          <th>발생</th>
                          <th>사용자</th>
                          <th>결과</th>
                          <th>사유</th>
                          <th>세션</th>
                          <th>Client IP</th>
                        </tr>
                      </thead>
                      <tbody>
                        {{auditRows}}
                      </tbody>
                    </table>
                  </div>
                </section>
              </main>
            </body>
            </html>
            """;
    }

    private static string FormatKoreaTime(DateTimeOffset time)
    {
        return time.ToString("yyyy-MM-dd HH:mm:ss 'KST'");
    }

    private static string FormatKoreaTime(DateTimeOffset? time)
    {
        return time is null ? "" : FormatKoreaTime(time.Value);
    }
}

