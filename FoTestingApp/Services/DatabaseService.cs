using FoTestingApp.Helpers;
using FoTestingApp.Models;
using MySqlConnector;
using Serilog;

namespace FoTestingApp.Services;

/// <summary>
/// Wrapper koneksi ke database OpenAccess MySQL.
/// Menyediakan metode untuk operasi user, customer, session, dan result.
/// </summary>
public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        _connectionString = ConfigManager.GetConnectionString();
    }

    private MySqlConnection CreateConnection() => new(_connectionString);

    /// <summary>Cek apakah koneksi ke DB bisa dibuka.</summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database connection test failed");
            return false;
        }
    }

    /// <summary>Ambil user berdasarkan email dari tabel `users` OpenAccess.</summary>
    public async Task<User?> GetUserByEmailAsync(string email)
    {
        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, name, email, password FROM users WHERE email = @email AND deleted_at IS NULL LIMIT 1";
            cmd.Parameters.AddWithValue("@email", email);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return new User
                {
                    Id = reader.GetInt32("id"),
                    Name = reader.GetString("name"),
                    Email = reader.GetString("email"),
                    PasswordHash = reader.GetString("password"),
                };
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetUserByEmail failed for email: {Email}", email);
            return null;
        }
    }

    /// <summary>Simpan pelanggan baru atau update jika customer_number sudah ada.</summary>
    public async Task<int> UpsertCustomerAsync(FoCustomer customer)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO fo_test_customers (customer_number, full_name, address, package_mbps, phone, email, technical_notes, created_at, updated_at)
            VALUES (@num, @name, @addr, @pkg, @phone, @email, @notes, NOW(), NOW())
            ON DUPLICATE KEY UPDATE
                full_name = VALUES(full_name),
                address = VALUES(address),
                package_mbps = VALUES(package_mbps),
                phone = VALUES(phone),
                email = VALUES(email),
                technical_notes = VALUES(technical_notes),
                updated_at = NOW();
            SELECT LAST_INSERT_ID();";

        cmd.Parameters.AddWithValue("@num", customer.CustomerNumber);
        cmd.Parameters.AddWithValue("@name", customer.FullName);
        cmd.Parameters.AddWithValue("@addr", customer.Address);
        cmd.Parameters.AddWithValue("@pkg", customer.PackageMbps);
        cmd.Parameters.AddWithValue("@phone", (object?)customer.Phone ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@email", (object?)customer.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@notes", (object?)customer.TechnicalNotes ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>Simpan sesi pengujian baru, return ID sesi yang dibuat.</summary>
    public async Task<int> CreateTestSessionAsync(FoTestSession session)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO fo_test_sessions (certification_id, customer_id, technician_id, test_date, overall_status, notes, created_at, updated_at)
            VALUES (@certId, @custId, @techId, @date, @status, @notes, NOW(), NOW());
            SELECT LAST_INSERT_ID();";

        cmd.Parameters.AddWithValue("@certId", session.CertificationId);
        cmd.Parameters.AddWithValue("@custId", session.CustomerId);
        cmd.Parameters.AddWithValue("@techId", session.TechnicianId);
        cmd.Parameters.AddWithValue("@date", session.TestDate);
        cmd.Parameters.AddWithValue("@status", session.OverallStatus.ToString().ToUpper());
        cmd.Parameters.AddWithValue("@notes", (object?)session.Notes ?? DBNull.Value);

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>Update status kelulusan keseluruhan sesi setelah semua test selesai.</summary>
    public async Task UpdateSessionStatusAsync(int sessionId, TestOverallStatus status)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE fo_test_sessions SET overall_status = @status, updated_at = NOW() WHERE id = @id";
        cmd.Parameters.AddWithValue("@status", status.ToString().ToUpper());
        cmd.Parameters.AddWithValue("@id", sessionId);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Simpan satu hasil test ke tabel fo_test_results.</summary>
    public async Task SaveTestResultAsync(FoTestResult result)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO fo_test_results (session_id, test_type, target, result_data, status, created_at, updated_at)
            VALUES (@sessionId, @type, @target, @data, @status, NOW(), NOW())";

        cmd.Parameters.AddWithValue("@sessionId", result.SessionId);
        cmd.Parameters.AddWithValue("@type", result.TestType);
        cmd.Parameters.AddWithValue("@target", result.Target);
        cmd.Parameters.AddWithValue("@data", result.ResultData?.RootElement.ToString() ?? "{}");
        cmd.Parameters.AddWithValue("@status", result.Status.ToString().ToUpper());

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Ambil daftar sesi pengujian untuk halaman Riwayat.</summary>
    public async Task<List<FoTestSession>> GetSessionsAsync(DateTime? fromDate = null, DateTime? toDate = null, int? technicianId = null, string? status = null)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        var where = new List<string>();
        if (fromDate.HasValue) { where.Add("s.test_date >= @fromDate"); }
        if (toDate.HasValue) { where.Add("s.test_date <= @toDate"); }
        if (technicianId.HasValue) { where.Add("s.technician_id = @techId"); }
        if (!string.IsNullOrEmpty(status)) { where.Add("s.overall_status = @status"); }

        var sql = $@"
            SELECT s.id, s.certification_id, s.customer_id, s.technician_id, s.test_date, s.overall_status, s.notes, s.created_at,
                   c.full_name as customer_name, c.customer_number, c.package_mbps,
                   u.name as technician_name
            FROM fo_test_sessions s
            JOIN fo_test_customers c ON c.id = s.customer_id
            JOIN users u ON u.id = s.technician_id
            {(where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "")}
            ORDER BY s.test_date DESC
            LIMIT 200";

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (fromDate.HasValue) { cmd.Parameters.AddWithValue("@fromDate", fromDate.Value); }
        if (toDate.HasValue) { cmd.Parameters.AddWithValue("@toDate", toDate.Value.AddDays(1)); }
        if (technicianId.HasValue) { cmd.Parameters.AddWithValue("@techId", technicianId.Value); }
        if (!string.IsNullOrEmpty(status)) { cmd.Parameters.AddWithValue("@status", status); }

        var sessions = new List<FoTestSession>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            sessions.Add(new FoTestSession
            {
                Id = reader.GetInt32("id"),
                CertificationId = reader.GetString("certification_id"),
                CustomerId = reader.GetInt32("customer_id"),
                TechnicianId = reader.GetInt32("technician_id"),
                TestDate = reader.GetDateTime("test_date"),
                OverallStatus = Enum.Parse<TestOverallStatus>(reader.GetString("overall_status"), true),
                Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString("notes"),
                CreatedAt = reader.GetDateTime("created_at"),
                Customer = new FoCustomer
                {
                    FullName = reader.GetString("customer_name"),
                    CustomerNumber = reader.GetString("customer_number"),
                    PackageMbps = reader.GetInt32("package_mbps"),
                },
                Technician = new User { Name = reader.GetString("technician_name") },
            });
        }

        return sessions;
    }

    /// <summary>Ambil semua hasil test untuk sebuah sesi.</summary>
    public async Task<List<FoTestResult>> GetResultsBySessionAsync(int sessionId)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, session_id, test_type, target, result_data, status, created_at FROM fo_test_results WHERE session_id = @id ORDER BY id";
        cmd.Parameters.AddWithValue("@id", sessionId);

        var results = new List<FoTestResult>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new FoTestResult
            {
                Id = reader.GetInt32("id"),
                SessionId = reader.GetInt32("session_id"),
                TestType = reader.GetString("test_type"),
                Target = reader.GetString("target"),
                Status = Enum.Parse<TestStatus>(reader.GetString("status"), true),
                CreatedAt = reader.GetDateTime("created_at"),
            });
        }

        return results;
    }

    /// <summary>Cari pelanggan berdasarkan nama, nomor pelanggan, atau alamat.</summary>
    public async Task<List<FoCustomer>> SearchCustomersAsync(string query)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, customer_number, full_name, address, package_mbps, phone, email
            FROM fo_test_customers
            WHERE full_name LIKE @q OR customer_number LIKE @q OR address LIKE @q
            ORDER BY full_name
            LIMIT 50";
        cmd.Parameters.AddWithValue("@q", $"%{query}%");

        var customers = new List<FoCustomer>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            customers.Add(new FoCustomer
            {
                Id = reader.GetInt32("id"),
                CustomerNumber = reader.GetString("customer_number"),
                FullName = reader.GetString("full_name"),
                Address = reader.GetString("address"),
                PackageMbps = reader.GetInt32("package_mbps"),
                Phone = reader.IsDBNull(reader.GetOrdinal("phone")) ? null : reader.GetString("phone"),
                Email = reader.IsDBNull(reader.GetOrdinal("email")) ? null : reader.GetString("email"),
            });
        }

        return customers;
    }
}
