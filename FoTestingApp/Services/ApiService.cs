using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using FoTestingApp.Services.Core;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>A call jinom-assets refused or could not be reached for; the message is for the technician.</summary>
public class ApiException(string message, HttpStatusCode? status = null, Exception? inner = null) : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>The token was refused (expired, or revoked by a login elsewhere): the user must log in again.</summary>
public sealed class ApiUnauthorizedException() : ApiException("Sesi login berakhir. Silakan login kembali.", HttpStatusCode.Unauthorized);

/// <summary>How a login went: the user, or why not.</summary>
public sealed record LoginOutcome(User? User, string? Error);

/// <summary>
/// The app's only way to jinom-assets (docs/PLATFORM_CONTRACT.md: it never reaches a database).
///
/// - One HttpClient for the whole app; the bearer token goes on each request, not on shared headers.
/// - A write the server does not accept throws (ApiException) — nothing that failed is reported as
///   saved. A read that fails throws too, so a screen can say so instead of showing an empty list.
/// - A refused token (401) ends the session (<see cref="SessionRejected"/>) instead of every call
///   quietly coming back empty.
/// - Times go out in the server's zone and come back in the laptop's (ServerTime).
/// - TLS certificates are validated: the password and token never travel to an unverified server.
/// </summary>
public class ApiService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new TolerantEnumConverter<TestOverallStatus>(TestOverallStatus.Fail),
            new TolerantEnumConverter<TestStatus>(TestStatus.Fail),
            new DateTimeConverter(),
            new NullableDateTimeConverter(),
        },
    };

    /// <summary>Raised when the server refuses the token; the shell sends the user back to login.</summary>
    public static event Action? SessionRejected;

    private readonly string _baseUrl = (ConfigManager.GetSetting("OpenAccessApiUrl") ?? "http://localhost/api/v1").Trim().TrimEnd('/');

    private static TimeZoneInfo ServerZone => ServerTime.ResolveZone(TestSettings.Require().ServerTimezone);

    private static string Wire(DateTime time) => ServerTime.ToWire(time, ServerZone);

    // ── Transport ───────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, bool authorized = true, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, $"{_baseUrl}/{path.TrimStart('/')}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (authorized && SessionManager.GetToken() is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Warning(ex, "{Method} {Path}: server unreachable", method, path);
            throw new ApiException($"Tidak dapat terhubung ke server ({_baseUrl}). Periksa koneksi internet.", null, ex);
        }

        if (authorized && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            SessionManager.Logout();
            SessionRejected?.Invoke();
            throw new ApiUnauthorizedException();
        }

        return response;
    }

    /// <summary>The response body as T; throws with the server's message when it is not a success.</summary>
    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, string what)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var message = await ServerMessageAsync(response);
                Log.Warning("{What} failed: {Status} {Message}", what, (int)response.StatusCode, message);
                throw new ApiException($"{what} gagal (HTTP {(int)response.StatusCode}){(message is null ? "" : $": {message}")}", response.StatusCode);
            }

            return await response.Content.ReadFromJsonAsync<T>(Json);
        }
    }

    private static async Task<string?> ServerMessageAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var first = errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray()).Select(v => v.GetString()).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(first)) return first;
            }
            return root.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ── Auth ────────────────────────────────────────────────────────────────────

    public async Task<LoginOutcome> LoginAsync(string email, string password)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendAsync(HttpMethod.Post, "auth/login", new
            {
                email,
                password,
                // One token per device: a login on another laptop must not revoke this one's.
                device_name = $"FoTestingDesktopApp-{Environment.MachineName}",
            }, authorized: false);
        }
        catch (ApiException ex)
        {
            return new LoginOutcome(null, ex.Message);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Login failed: {Status} for {Email}", (int)response.StatusCode, email);
                var error = (int)response.StatusCode switch
                {
                    422 => "Email atau password salah. Silakan coba lagi.",
                    423 => "Akun ini memakai verifikasi dua langkah (2FA), yang belum didukung aplikasi ini.",
                    429 => "Terlalu banyak percobaan login. Tunggu beberapa menit lalu coba lagi.",
                    403 => "Akun ini tidak diizinkan login.",
                    _ => $"Server menolak login (HTTP {(int)response.StatusCode}).",
                };
                return new LoginOutcome(null, error);
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
            if (result?.User is null || string.IsNullOrEmpty(result.Token))
            {
                return new LoginOutcome(null, "Respons login dari server tidak lengkap.");
            }

            var user = new User
            {
                Id = result.User.Id,
                Name = result.User.Name ?? string.Empty,
                Email = result.User.Email ?? email,
                CompanyId = result.User.CompanyId,
            };
            SessionManager.Login(user, result.Token);
            return new LoginOutcome(user, null);
        }
    }

    /// <summary>Revokes the token on the server; the local session ends whatever the answer.</summary>
    public async Task LogoutAsync()
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "auth/logout");
        }
        catch (ApiException ex)
        {
            Log.Information(ex, "Logout could not reach the server; the token expires on its own");
        }
    }

    // ── Test parameters ─────────────────────────────────────────────────────────

    /// <summary>
    /// The test parameters set in Jinom Assets (Integrasi → Jinom Test), made current for the whole
    /// app. Read after login and before every run, so a change there applies to the next test.
    /// </summary>
    public async Task<TestSettings> LoadTestSettingsAsync(CancellationToken ct = default)
    {
        var envelope = await ReadAsync<DataEnvelope<TestSettings>>(
            await SendAsync(HttpMethod.Get, "reseller-certification/fo-test/settings", ct: ct), "Memuat pengaturan pengujian");
        if (envelope?.Data is not { } settings || string.IsNullOrWhiteSpace(settings.ServerTimezone))
        {
            throw new ApiException("Memuat pengaturan pengujian gagal: jawaban server tidak lengkap.");
        }

        TestSettings.Use(settings);
        return TestSettings.Require();
    }

    // ── Sites ───────────────────────────────────────────────────────────────────

    /// <summary>The company's customers matching a name or service number (first 50).</summary>
    public async Task<List<FoCustomer>> SearchCustomersAsync(string query, CancellationToken ct = default)
    {
        var path = "infrastructure/subscribers?per_page=50";
        if (!string.IsNullOrWhiteSpace(query))
        {
            path += $"&search={Uri.EscapeDataString(query.Trim())}";
        }

        var page = await ReadAsync<PaginatedResponse<ApiSubscriber>>(await SendAsync(HttpMethod.Get, path, ct: ct), "Memuat pelanggan");
        return page?.Data?
            .Select(sub => new FoCustomer
            {
                SiteType = "customer",
                // The server matches site_id against the subscriber's id, not its service number.
                SiteId = sub.Id.ToString(),
                ServiceNumber = sub.NomorLayanan,
                FullName = sub.Nama ?? string.Empty,
                Address = sub.Alamat ?? string.Empty,
                // From the customer's data only: the technician does not type a package.
                PackageMbps = PackageSpeed.ForService(sub.BandwidthDown, sub.Paket),
                PackageName = sub.Paket,
                Email = sub.Email,
            })
            .ToList() ?? [];
    }

    /// <summary>The company's POPs, every page of them (a partner has few: they are listed, not searched).</summary>
    public async Task<List<PopOption>> GetAvailablePopsAsync(CancellationToken ct = default)
    {
        const int perPage = 200;
        const int maxPages = 20;
        var pops = new List<PopOption>();

        for (var page = 1; page <= maxPages; page++)
        {
            var result = await ReadAsync<PaginatedResponse<ApiPop>>(
                await SendAsync(HttpMethod.Get, $"infrastructure/pops?per_page={perPage}&page={page}", ct: ct), "Memuat POP");
            pops.AddRange(result?.Data?
                .Where(pop => !string.IsNullOrWhiteSpace(pop.Name))
                .Select(pop => new PopOption { SiteId = pop.Id.ToString(), Code = pop.Code, Name = pop.Name!, Address = pop.Address }) ?? []);

            if (result?.Meta is not { } meta || meta.CurrentPage >= meta.LastPage)
            {
                break;
            }
        }

        return pops.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    // ── FO test (frozen ResellerCertification API: request keys as the server knows them) ─────

    public async Task<int> UpsertCustomerAsync(FoCustomer customer)
    {
        var saved = await ReadAsync<FoCustomer>(await SendAsync(HttpMethod.Post, "reseller-certification/fo-test/customers/upsert", new
        {
            site_type = customer.SiteType,
            site_id = customer.SiteId,
            full_name = customer.FullName,
            address = customer.Address,
            // A POP has no package: nothing is stored rather than an invented speed.
            package_mbps = customer.PackageMbps > 0 ? customer.PackageMbps : (int?)null,
            // The server only takes a well-formed address; billing sometimes holds "-" or a name.
            email = System.Net.Mail.MailAddress.TryCreate(customer.Email?.Trim(), out var mail) ? mail.Address : null,
            technical_notes = customer.TechnicalNotes,
        }), "Menyimpan data pelanggan");

        return saved?.Id is > 0 ? saved.Id : throw new ApiException("Menyimpan data pelanggan gagal: server tidak mengembalikan id.");
    }

    public async Task<int> CreateTestSessionAsync(FoTestSession session)
    {
        var saved = await ReadAsync<FoTestSession>(await SendAsync(HttpMethod.Post, "reseller-certification/fo-test/sessions", new
        {
            certification_id = session.CertificationId,
            customer_id = session.CustomerId,
            technician_id = session.TechnicianId > 0 ? session.TechnicianId : (int?)null,
            test_date = Wire(session.TestDate),
            start_time = session.StartTime is { } start ? Wire(start) : null,
            end_time = session.EndTime is { } end ? Wire(end) : null,
            overall_status = session.OverallStatus.ToString().ToUpperInvariant(),
            notes = session.Notes,
        }), "Membuat sesi pengujian");

        return saved?.Id is > 0 ? saved.Id : throw new ApiException("Membuat sesi pengujian gagal: server tidak mengembalikan id.");
    }

    /// <summary>Sets a session's final status; notes are only sent when given, so existing ones stay.</summary>
    public async Task UpdateSessionStatusAsync(int sessionId, TestOverallStatus status, DateTime? endTime = null, string? notes = null)
    {
        await ReadAsync<JsonElement>(await SendAsync(HttpMethod.Patch, $"reseller-certification/fo-test/sessions/{sessionId}/status", new
        {
            overall_status = status.ToString().ToUpperInvariant(),
            notes,
            end_time = endTime is { } end ? Wire(end) : null,
        }), "Menyimpan status sesi");
    }

    public async Task SaveTestResultAsync(FoTestResult result)
    {
        await ReadAsync<JsonElement>(await SendAsync(HttpMethod.Post, "reseller-certification/fo-test/results", new
        {
            session_id = result.SessionId,
            test_type = result.TestType,
            target = result.Target,
            result_data = result.ResultData?.RootElement,
            status = result.Status.ToString().ToUpperInvariant(),
        }), $"Menyimpan hasil {ResultFormatter.TestName(result.TestType)}");
    }

    /// <summary>
    /// Sessions, newest first. The server returns at most <see cref="SessionListLimit"/>; a list that
    /// long may be cut off, which the dashboard says.
    /// </summary>
    public async Task<List<FoTestSession>> GetSessionsAsync(DateTime? fromDate = null, DateTime? toDate = null, int? technicianId = null, string? status = null)
    {
        var query = new List<string>();
        if (fromDate.HasValue) query.Add($"from_date={fromDate.Value:yyyy'-'MM'-'dd}");
        if (toDate.HasValue) query.Add($"to_date={toDate.Value:yyyy'-'MM'-'dd}");
        if (technicianId.HasValue) query.Add($"technician_id={technicianId.Value}");
        if (!string.IsNullOrEmpty(status)) query.Add($"status={Uri.EscapeDataString(status)}");

        var path = "reseller-certification/fo-test/sessions" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
        return await ReadAsync<List<FoTestSession>>(await SendAsync(HttpMethod.Get, path), "Memuat riwayat pengujian") ?? [];
    }

    public const int SessionListLimit = 200;

    /// <summary>One session with its customer, technician and results.</summary>
    public async Task<FoTestSession?> GetSessionAsync(int sessionId) =>
        await ReadAsync<FoTestSession>(await SendAsync(HttpMethod.Get, $"reseller-certification/fo-test/sessions/{sessionId}"), "Memuat sesi pengujian");

    public async Task<List<FoTestResult>> GetResultsBySessionAsync(int sessionId) =>
        await ReadAsync<List<FoTestResult>>(await SendAsync(HttpMethod.Get, $"reseller-certification/fo-test/results?session_id={sessionId}"), "Memuat hasil pengujian") ?? [];

    // ── Wire types ──────────────────────────────────────────────────────────────

    private sealed class DateTimeConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String ? ServerTime.FromWire(reader.GetString(), ServerZone) ?? default : default;

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(ServerTime.ToWire(value, ServerZone));
    }

    private sealed class NullableDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String ? ServerTime.FromWire(reader.GetString(), ServerZone) : null;

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteStringValue(ServerTime.ToWire(value.Value, ServerZone));
            else writer.WriteNullValue();
        }
    }
}

public class LoginResponse
{
    public string? Token { get; set; }
    public OpenAccessUser? User { get; set; }
}

public class OpenAccessUser
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public int? CompanyId { get; set; }
}

public class PopOption
{
    public string SiteId { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }

    public string DisplayName => Name;
}

public class ApiPop
{
    public int Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Address { get; set; }
}

public class ApiSubscriber
{
    public int Id { get; set; }
    public string? NomorLayanan { get; set; }
    public string? Nama { get; set; }
    public string? Email { get; set; }
    public string? Alamat { get; set; }
    public string? Paket { get; set; }
    public string? BandwidthDown { get; set; }
    public string? BandwidthUp { get; set; }
}

public class DataEnvelope<T>
{
    public T? Data { get; set; }
}

public class PaginatedResponse<T>
{
    public List<T>? Data { get; set; }
    public PageMeta? Meta { get; set; }
}

public class PageMeta
{
    public int CurrentPage { get; set; }
    public int LastPage { get; set; }
    public int Total { get; set; }
}
