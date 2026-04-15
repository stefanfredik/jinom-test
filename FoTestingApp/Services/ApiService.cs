using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FoTestingApp.Helpers;
using FoTestingApp.Models;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Service untuk komunikasi dengan REST API OpenAccess.
/// Menggantikan DatabaseService lama yang terkoneksi langsung ke MySQL.
/// </summary>
public class ApiService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private static readonly JsonSerializerOptions _snakeCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(), new DateTimeConverter(), new NullableDateTimeConverter() }
    };

    public ApiService()
    {
        _baseUrl = ConfigManager.GetSetting("OpenAccessApiUrl") ?? "http://localhost/api/v1";
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true // Abai SSL errors untuk testing ke local IP
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl)
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private void SetAuthorizationHeader()
    {
        var token = SessionManager.GetToken();
        if (!string.IsNullOrEmpty(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else
        {
            Log.Warning("SetAuthorizationHeader: Token is null or empty");
        }
    }

    /// <summary>Cek apakah API server bisa dijangkau (Login test).</summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/auth/user");
            return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "API connection test failed");
            return false;
        }
    }

    public async Task<User?> LoginAsync(string email, string password)
    {
        try
        {
            var request = new
            {
                email = email,
                password = password,
                device_name = "FoTestingDesktopApp"
            };

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/auth/login", request);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Login API failed: {Status} for email: {Email}", response.StatusCode, email);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(_snakeCaseOptions);
            if (result?.User != null && result.Token != null)
            {
                var user = new User
                {
                    Id = result.User.Id,
                    Name = result.User.Name,
                    Email = result.User.Email,
                    CompanyId = result.User.CompanyId
                };
                
                SessionManager.Login(user, result.Token);
                return user;
            }
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "LoginAsync failed for email: {Email}", email);
            return null;
        }
    }
    
    public async Task<List<FoCustomer>> SearchCustomersAsync(string query, string type = "customer")
    {
        SetAuthorizationHeader();
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/reseller-certification/fo-test/customers/search?q={Uri.EscapeDataString(query)}&type={Uri.EscapeDataString(type)}");
            if (response.IsSuccessStatusCode)
            {
                var customers = await response.Content.ReadFromJsonAsync<List<FoCustomer>>(_snakeCaseOptions);
                return customers ?? new List<FoCustomer>();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SearchCustomersAsync failed for query: {Query}", query);
        }
        return new List<FoCustomer>();
    }

    public async Task<int> UpsertCustomerAsync(FoCustomer customer)
    {
        SetAuthorizationHeader();
        var content = JsonContent.Create(customer, options: _snakeCaseOptions);
        var response = await _httpClient.PostAsync($"{_baseUrl}/reseller-certification/fo-test/customers/upsert", content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Log.Error("UpsertCustomerAsync failed: {Status} - {Body}", response.StatusCode, body);
            throw new HttpRequestException($"Gagal menyimpan data pelanggan (HTTP {(int)response.StatusCode})");
        }
        var savedCustomer = await response.Content.ReadFromJsonAsync<FoCustomer>(_snakeCaseOptions);
        return savedCustomer?.Id ?? 0;
    }

    public async Task<int> CreateTestSessionAsync(FoTestSession session)
    {
        SetAuthorizationHeader();
        var request = new
        {
            certification_id = session.CertificationId,
            customer_id = session.CustomerId,
            technician_id = session.TechnicianId,
            test_date = session.TestDate.ToString("yyyy-MM-dd HH:mm:ss"),
            start_time = session.StartTime?.ToString("yyyy-MM-dd HH:mm:ss"),
            end_time = session.EndTime?.ToString("yyyy-MM-dd HH:mm:ss"),
            overall_status = session.OverallStatus.ToString().ToUpper(),
            notes = session.Notes
        };
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/reseller-certification/fo-test/sessions", request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Log.Error("CreateTestSessionAsync failed: {Status} - {Body}", response.StatusCode, body);
            throw new HttpRequestException($"Gagal membuat sesi pengujian (HTTP {(int)response.StatusCode})");
        }
        var savedSession = await response.Content.ReadFromJsonAsync<FoTestSession>(_snakeCaseOptions);
        return savedSession?.Id ?? 0;
    }

    public async Task UpdateSessionStatusAsync(int sessionId, TestOverallStatus status, DateTime? endTime = null, string? notes = null)
    {
        SetAuthorizationHeader();
        var request = new { 
            overall_status = status.ToString().ToUpper(), 
            notes = notes,
            end_time = endTime?.ToString("yyyy-MM-dd HH:mm:ss")
        };
        var requestMessage = new HttpRequestMessage(new HttpMethod("PATCH"), $"{_baseUrl}/reseller-certification/fo-test/sessions/{sessionId}/status")
        {
            Content = JsonContent.Create(request)
        };
        
        var response = await _httpClient.SendAsync(requestMessage);
        if (!response.IsSuccessStatusCode)
        {
            Log.Error("UpdateSessionStatusAsync failed: {Status} for session {SessionId}", response.StatusCode, sessionId);
        }
    }

    public async Task SaveTestResultAsync(FoTestResult result)
    {
        SetAuthorizationHeader();
        var request = new
        {
            session_id = result.SessionId,
            test_type = result.TestType,
            target = result.Target,
            result_data = result.ResultData, // Assuming this maps to JSON string or JsonElement nicely with System.Net.Http.Json
            status = result.Status.ToString().ToUpper()
        };
        var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/reseller-certification/fo-test/results", request);
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("SaveTestResultAsync failed: {Status} for test {TestType}", response.StatusCode, result.TestType);
        }
    }

    public async Task<List<FoTestSession>> GetSessionsAsync(DateTime? fromDate = null, DateTime? toDate = null, int? technicianId = null, string? status = null)
    {
        SetAuthorizationHeader();
        var queryParams = new List<string>();
        if (fromDate.HasValue) queryParams.Add($"from_date={fromDate.Value:yyyy-MM-dd}");
        if (toDate.HasValue) queryParams.Add($"to_date={toDate.Value:yyyy-MM-dd}");
        if (technicianId.HasValue) queryParams.Add($"technician_id={technicianId.Value}");
        if (!string.IsNullOrEmpty(status)) queryParams.Add($"status={status}");

        var queryString = string.Join("&", queryParams);
        var url = $"{_baseUrl}/reseller-certification/fo-test/sessions";
        if (!string.IsNullOrEmpty(queryString)) url += "?" + queryString;

        var response = await _httpClient.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            var sessions = await response.Content.ReadFromJsonAsync<List<FoTestSession>>(_snakeCaseOptions);
            return sessions ?? new List<FoTestSession>();
        }
        return new List<FoTestSession>();
    }

    public async Task<List<FoTestResult>> GetResultsBySessionAsync(int sessionId)
    {
        SetAuthorizationHeader();
        var response = await _httpClient.GetAsync($"{_baseUrl}/reseller-certification/fo-test/results?session_id={sessionId}");
        if (response.IsSuccessStatusCode)
        {
            var results = await response.Content.ReadFromJsonAsync<List<FoTestResult>>(_snakeCaseOptions);
            return results ?? new List<FoTestResult>();
        }
        return new List<FoTestResult>();
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

public class DateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (str == null) return default;
        if (DateTime.TryParse(str, out var dt)) return dt;
        return default;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}

public class NullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (string.IsNullOrWhiteSpace(str)) return null;
        if (DateTime.TryParse(str, out var dt)) return dt;
        return null;
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        else writer.WriteNullValue();
    }
}
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (str == null) return default;
        if (DateTime.TryParse(str, out var dt)) return dt;
        return default;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
