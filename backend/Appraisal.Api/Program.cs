using System.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Npgsql;
using NpgsqlTypes;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "nca_appraisal_session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.LoginPath = "/api/auth/login";
        options.AccessDeniedPath = "/api/auth/forbidden";
    });

builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
        {
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }
    });
});

builder.Services.AddSingleton(_ =>
{
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
    return NpgsqlDataSource.Create(connectionString);
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors("AppCors");
app.UseAuthentication();
app.UseAuthorization();

var storageRoot = Path.GetFullPath(builder.Configuration["Storage:RootPath"] ?? "App_Data/attachments");
Directory.CreateDirectory(storageRoot);

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    app = "NCA Performance Appraisal API",
    storageRoot
}));

app.MapPost("/api/bootstrap/admin", async (
    BootstrapAdminRequest request,
    NpgsqlDataSource db,
    IConfiguration config,
    HttpContext http) =>
{
    var expectedKey = config["Bootstrap:Key"];
    if (string.IsNullOrWhiteSpace(expectedKey) || !CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expectedKey),
            System.Text.Encoding.UTF8.GetBytes(request.BootstrapKey)))
    {
        return Results.Forbid();
    }

    await using var conn = await db.OpenConnectionAsync();
    await using (var countCmd = new NpgsqlCommand("select count(*) from users", conn))
    {
        var count = (long)(await countCmd.ExecuteScalarAsync() ?? 0L);
        if (count > 0) return Results.Conflict(new { message = "Bootstrap is only allowed before the first user is created." });
    }

    var password = PasswordHasher.Hash(request.Password);
    var id = Guid.NewGuid();
    await using var cmd = new NpgsqlCommand("""
        insert into users (id, email, display_name, role, division, unit, password_hash, password_salt, password_iterations, is_active)
        values (@id, @email, @displayName, 'systemAdmin', @division, @unit, @hash, @salt, @iterations, true)
        """, conn);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("email", request.Email.Trim().ToLowerInvariant());
    cmd.Parameters.AddWithValue("displayName", request.DisplayName.Trim());
    cmd.Parameters.AddWithValue("division", request.Division ?? "All Divisions");
    cmd.Parameters.AddWithValue("unit", request.Unit ?? "All Units");
    cmd.Parameters.AddWithValue("hash", password.Hash);
    cmd.Parameters.AddWithValue("salt", password.Salt);
    cmd.Parameters.AddWithValue("iterations", password.Iterations);
    await cmd.ExecuteNonQueryAsync();

    await AuditAsync(conn, id, "bootstrap.admin.created", "users", id.ToString(), http.Connection.RemoteIpAddress?.ToString());
    return Results.Created($"/api/users/{id}", new { id, request.Email, role = "systemAdmin" });
});

app.MapPost("/api/auth/login", async (LoginRequest request, NpgsqlDataSource db, HttpContext http) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var user = await FindUserByEmailAsync(conn, request.Email);
    if (user is null || !user.IsActive || !PasswordHasher.Verify(request.Password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations))
    {
        return Results.Unauthorized();
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Email, user.Email),
        new(ClaimTypes.Name, user.DisplayName),
        new(ClaimTypes.Role, user.Role),
        new("division", user.Division ?? ""),
        new("unit", user.Unit ?? "")
    };

    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

    await AuditAsync(conn, user.Id, "auth.login", "users", user.Id.ToString(), http.Connection.RemoteIpAddress?.ToString());
    return Results.Ok(ToUserResponse(user));
});

app.MapPost("/api/auth/register", async (RegisterRequest request, NpgsqlDataSource db, HttpContext http) =>
{
    if (request.Password.Length < 6) return Results.BadRequest(new { message = "Password must be at least 6 characters." });

    await using var conn = await db.OpenConnectionAsync();
    if (await FindUserByEmailAsync(conn, request.Email) is not null)
    {
        return Results.Conflict(new { message = "An account already exists for this email." });
    }

    var password = PasswordHasher.Hash(request.Password);
    var id = Guid.NewGuid();
    await using var tx = await conn.BeginTransactionAsync();
    await using (var cmd = new NpgsqlCommand("""
        insert into users (id, email, display_name, role, division, unit, password_hash, password_salt, password_iterations, is_active)
        values (@id, @email, @displayName, 'employee', null, null, @hash, @salt, @iterations, true)
        """, conn, tx))
    {
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", request.Email.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("displayName", request.DisplayName.Trim());
        cmd.Parameters.AddWithValue("hash", password.Hash);
        cmd.Parameters.AddWithValue("salt", password.Salt);
        cmd.Parameters.AddWithValue("iterations", password.Iterations);
        await cmd.ExecuteNonQueryAsync();
    }

    await using (var dashCmd = new NpgsqlCommand("""
        insert into dashboards (user_id, data)
        values (@userId, '{"objectivesData":[]}'::jsonb)
        """, conn, tx))
    {
        dashCmd.Parameters.AddWithValue("userId", id);
        await dashCmd.ExecuteNonQueryAsync();
    }

    await AuditAsync(conn, id, "auth.register", "users", id.ToString(), http.Connection.RemoteIpAddress?.ToString(), tx);
    await tx.CommitAsync();
    return Results.Created($"/api/users/{id}", new { id, email = request.Email, role = "employee" });
});

app.MapPost("/api/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/auth/me", async (HttpContext http, NpgsqlDataSource db) =>
{
    var userId = http.User.UserId();
    await using var conn = await db.OpenConnectionAsync();
    var user = await FindUserByIdAsync(conn, userId);
    return user is null ? Results.NotFound() : Results.Ok(ToUserResponse(user));
}).RequireAuthorization();

app.MapGet("/api/auth/forbidden", () => Results.Forbid());

app.MapGet("/api/dashboard/me", async (HttpContext http, NpgsqlDataSource db) =>
{
    var userId = http.User.UserId();
    await using var conn = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("select data from dashboards where user_id = @userId", conn);
    cmd.Parameters.AddWithValue("userId", userId);
    var data = await cmd.ExecuteScalarAsync();
    return data is null || data is DBNull ? Results.Ok(new { objectivesData = Array.Empty<object>() }) : Results.Text(data.ToString()!, "application/json");
}).RequireAuthorization();

app.MapPut("/api/dashboard/me", async (JsonElement dashboardData, HttpContext http, NpgsqlDataSource db) =>
{
    var userId = http.User.UserId();
    await using var conn = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        insert into dashboards (user_id, data, updated_at)
        values (@userId, @data, now())
        on conflict (user_id) do update set data = excluded.data, updated_at = now()
        """, conn);
    cmd.Parameters.AddWithValue("userId", userId);
    cmd.Parameters.Add("data", NpgsqlDbType.Jsonb).Value = dashboardData.GetRawText();
    await cmd.ExecuteNonQueryAsync();
    await AuditAsync(conn, userId, "dashboard.save", "dashboards", userId.ToString(), http.Connection.RemoteIpAddress?.ToString());
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/dashboard/{userId:guid}", async (Guid userId, HttpContext http, NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var currentUser = await FindUserByIdAsync(conn, http.User.UserId());
    var targetUser = await FindUserByIdAsync(conn, userId);
    if (currentUser is null || targetUser is null) return Results.NotFound();
    if (currentUser.Id != targetUser.Id && !currentUser.CanManageUser(targetUser)) return Results.Forbid();

    await using var cmd = new NpgsqlCommand("select data from dashboards where user_id = @userId", conn);
    cmd.Parameters.AddWithValue("userId", userId);
    var data = await cmd.ExecuteScalarAsync();
    return data is null || data is DBNull ? Results.Ok(new { objectivesData = Array.Empty<object>() }) : Results.Text(data.ToString()!, "application/json");
}).RequireAuthorization();

app.MapGet("/api/users", async (HttpContext http, NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var currentUser = await FindUserByIdAsync(conn, http.User.UserId());
    if (currentUser is null) return Results.Unauthorized();

    var sql = currentUser.CanManageAll()
        ? "select id, email, display_name, role, division, unit, is_active, created_at, updated_at from users order by display_name"
        : "select id, email, display_name, role, division, unit, is_active, created_at, updated_at from users where division = @division order by display_name";
    await using var cmd = new NpgsqlCommand(sql, conn);
    if (!currentUser.CanManageAll()) cmd.Parameters.AddWithValue("division", currentUser.Division ?? "");

    var users = new List<UserResponse>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        users.Add(new UserResponse(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetBoolean(6),
            reader.GetDateTime(7),
            reader.GetDateTime(8)));
    }

    return Results.Ok(users);
}).RequireAuthorization();

app.MapPost("/api/users", async (UserCreateRequest request, HttpContext http, NpgsqlDataSource db) =>
{
    if (request.Password.Length < 8) return Results.BadRequest(new { message = "Password must be at least 8 characters." });
    if (!Roles.ManagerAssignable.Contains(request.Role)) return Results.BadRequest(new { message = "Role is not assignable by organization managers." });

    var email = request.Email.Trim().ToLowerInvariant();
    var displayName = request.DisplayName.Trim();
    var division = request.Division.Trim();
    var unit = request.Unit.Trim();
    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName) ||
        string.IsNullOrWhiteSpace(division) || string.IsNullOrWhiteSpace(unit))
    {
        return Results.BadRequest(new { message = "Email, display name, division, and unit are required." });
    }

    await using var conn = await db.OpenConnectionAsync();
    var currentUser = await FindUserByIdAsync(conn, http.User.UserId());
    if (currentUser is null) return Results.Unauthorized();
    if (!currentUser.CanManageDivision(division)) return Results.Forbid();
    if (await FindUserByEmailAsync(conn, email) is not null)
    {
        return Results.Conflict(new { message = "An account already exists for this email." });
    }

    var password = PasswordHasher.Hash(request.Password);
    var id = Guid.NewGuid();
    await using var tx = await conn.BeginTransactionAsync();
    await using (var cmd = new NpgsqlCommand("""
        insert into users (id, email, display_name, role, division, unit, password_hash, password_salt, password_iterations, is_active)
        values (@id, @email, @displayName, @role, @division, @unit, @hash, @salt, @iterations, true)
        """, conn, tx))
    {
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("displayName", displayName);
        cmd.Parameters.AddWithValue("role", request.Role);
        cmd.Parameters.AddWithValue("division", division);
        cmd.Parameters.AddWithValue("unit", unit);
        cmd.Parameters.AddWithValue("hash", password.Hash);
        cmd.Parameters.AddWithValue("salt", password.Salt);
        cmd.Parameters.AddWithValue("iterations", password.Iterations);
        await cmd.ExecuteNonQueryAsync();
    }

    await using (var dashCmd = new NpgsqlCommand("""
        insert into dashboards (user_id, data)
        values (@userId, '{"objectivesData":[]}'::jsonb)
        """, conn, tx))
    {
        dashCmd.Parameters.AddWithValue("userId", id);
        await dashCmd.ExecuteNonQueryAsync();
    }

    await AuditAsync(conn, currentUser.Id, "user.created", "users", id.ToString(), http.Connection.RemoteIpAddress?.ToString(), tx);
    await tx.CommitAsync();

    return Results.Created($"/api/users/{id}", new UserResponse(id, email, displayName, request.Role, division, unit, true, DateTime.UtcNow, DateTime.UtcNow));
}).RequireAuthorization();

app.MapPatch("/api/users/{userId:guid}/assignment", async (
    Guid userId,
    AssignmentRequest request,
    HttpContext http,
    NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var currentUser = await FindUserByIdAsync(conn, http.User.UserId());
    var targetUser = await FindUserByIdAsync(conn, userId);
    if (currentUser is null || targetUser is null) return Results.NotFound();
    if (!currentUser.CanManageUser(targetUser)) return Results.Forbid();
    if (!Roles.ManagerAssignable.Contains(request.Role)) return Results.BadRequest(new { message = "Role is not assignable by organization managers." });

    await using var cmd = new NpgsqlCommand("""
        update users
        set division = @division, unit = @unit, role = @role, updated_at = now()
        where id = @id
        """, conn);
    cmd.Parameters.AddWithValue("id", userId);
    cmd.Parameters.AddWithValue("division", request.Division);
    cmd.Parameters.AddWithValue("unit", request.Unit);
    cmd.Parameters.AddWithValue("role", request.Role);
    await cmd.ExecuteNonQueryAsync();
    await AuditAsync(conn, currentUser.Id, "user.assignment.update", "users", userId.ToString(), http.Connection.RemoteIpAddress?.ToString());
    return Results.NoContent();
}).RequireAuthorization();

app.MapPatch("/api/users/me/profile", async (
    SelfProfileRequest request,
    HttpContext http,
    NpgsqlDataSource db) =>
{
    var userId = http.User.UserId();
    await using var conn = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        update users
        set division = @division, unit = @unit, updated_at = now()
        where id = @id and role = 'employee'
        """, conn);
    cmd.Parameters.AddWithValue("id", userId);
    cmd.Parameters.AddWithValue("division", request.Division);
    cmd.Parameters.AddWithValue("unit", request.Unit);
    var changed = await cmd.ExecuteNonQueryAsync();
    if (changed == 0) return Results.Forbid();
    await AuditAsync(conn, userId, "user.profile.self_update", "users", userId.ToString(), http.Connection.RemoteIpAddress?.ToString());
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/units", async (string? division, NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var sql = string.IsNullOrWhiteSpace(division)
        ? "select id, name, division, active from units order by division, name"
        : "select id, name, division, active from units where division = @division order by name";
    await using var cmd = new NpgsqlCommand(sql, conn);
    if (!string.IsNullOrWhiteSpace(division)) cmd.Parameters.AddWithValue("division", division);

    var units = new List<UnitResponse>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        units.Add(new UnitResponse(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3)));
    }
    return Results.Ok(units);
}).RequireAuthorization();

app.MapPost("/api/units", async (UnitCreateRequest request, HttpContext http, NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var currentUser = await FindUserByIdAsync(conn, http.User.UserId());
    if (currentUser is null) return Results.Unauthorized();
    if (!currentUser.CanManageDivision(request.Division)) return Results.Forbid();

    var id = Guid.NewGuid();
    await using var cmd = new NpgsqlCommand("""
        insert into units (id, name, division, active, created_by)
        values (@id, @name, @division, true, @createdBy)
        on conflict (division, name) do update set active = true
        """, conn);
    cmd.Parameters.AddWithValue("id", id);
    cmd.Parameters.AddWithValue("name", request.Name.Trim());
    cmd.Parameters.AddWithValue("division", request.Division.Trim());
    cmd.Parameters.AddWithValue("createdBy", currentUser.Id);
    await cmd.ExecuteNonQueryAsync();
    await AuditAsync(conn, currentUser.Id, "unit.upsert", "units", request.Name, http.Connection.RemoteIpAddress?.ToString());
    return Results.Ok(new { id, request.Name, request.Division });
}).RequireAuthorization();

app.MapPatch("/api/units/{unitId:guid}/archive", async (Guid unitId, HttpContext http, NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var currentUser = await FindUserByIdAsync(conn, http.User.UserId());
    if (currentUser is null) return Results.Unauthorized();

    await using (var lookup = new NpgsqlCommand("select division from units where id = @id", conn))
    {
        lookup.Parameters.AddWithValue("id", unitId);
        var division = await lookup.ExecuteScalarAsync();
        if (division is null) return Results.NotFound();
        if (!currentUser.CanManageDivision((string)division)) return Results.Forbid();
    }

    await using var cmd = new NpgsqlCommand("update units set active = false where id = @id", conn);
    cmd.Parameters.AddWithValue("id", unitId);
    await cmd.ExecuteNonQueryAsync();
    await AuditAsync(conn, currentUser.Id, "unit.archive", "units", unitId.ToString(), http.Connection.RemoteIpAddress?.ToString());
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/attachments", async (
    IFormFile file,
    string objectiveId,
    string taskType,
    int taskIndex,
    HttpContext http,
    NpgsqlDataSource db) =>
{
    if (file.Length == 0) return Results.BadRequest(new { message = "Empty attachment." });
    if (file.Length > 10 * 1024 * 1024) return Results.BadRequest(new { message = "Maximum attachment size is 10 MB." });

    var userId = http.User.UserId();
    var attachmentId = Guid.NewGuid();
    var safeFileName = FileName.Sanitize(file.FileName);
    var userFolder = Path.Combine(storageRoot, userId.ToString("N"));
    Directory.CreateDirectory(userFolder);
    var diskPath = Path.Combine(userFolder, $"{attachmentId:N}-{safeFileName}");

    await using (var stream = File.Create(diskPath))
    {
        await file.CopyToAsync(stream);
    }

    await using var conn = await db.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("""
        insert into attachments (id, owner_user_id, objective_id, task_type, task_index, original_file_name, content_type, size_bytes, storage_path, created_by)
        values (@id, @ownerUserId, @objectiveId, @taskType, @taskIndex, @originalFileName, @contentType, @sizeBytes, @storagePath, @createdBy)
        """, conn);
    cmd.Parameters.AddWithValue("id", attachmentId);
    cmd.Parameters.AddWithValue("ownerUserId", userId);
    cmd.Parameters.AddWithValue("objectiveId", objectiveId);
    cmd.Parameters.AddWithValue("taskType", taskType);
    cmd.Parameters.AddWithValue("taskIndex", taskIndex);
    cmd.Parameters.AddWithValue("originalFileName", file.FileName);
    cmd.Parameters.AddWithValue("contentType", file.ContentType);
    cmd.Parameters.AddWithValue("sizeBytes", file.Length);
    cmd.Parameters.AddWithValue("storagePath", diskPath);
    cmd.Parameters.AddWithValue("createdBy", userId);
    await cmd.ExecuteNonQueryAsync();
    await AuditAsync(conn, userId, "attachment.upload", "attachments", attachmentId.ToString(), http.Connection.RemoteIpAddress?.ToString());

    return Results.Ok(new AttachmentResponse(attachmentId, file.FileName, file.ContentType, file.Length, $"/api/attachments/{attachmentId}/download"));
}).RequireAuthorization().DisableAntiforgery();

app.MapGet("/api/attachments/{attachmentId:guid}/download", async (Guid attachmentId, HttpContext http, NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var currentUser = await FindUserByIdAsync(conn, http.User.UserId());
    var attachment = await FindAttachmentAsync(conn, attachmentId);
    if (currentUser is null || attachment is null) return Results.NotFound();
    if (attachment.OwnerUserId != currentUser.Id && !currentUser.CanManageAll()) return Results.Forbid();
    if (!File.Exists(attachment.StoragePath)) return Results.NotFound();

    return Results.File(attachment.StoragePath, attachment.ContentType, attachment.OriginalFileName);
}).RequireAuthorization();

app.MapDelete("/api/attachments/{attachmentId:guid}", async (Guid attachmentId, HttpContext http, NpgsqlDataSource db) =>
{
    await using var conn = await db.OpenConnectionAsync();
    var currentUser = await FindUserByIdAsync(conn, http.User.UserId());
    var attachment = await FindAttachmentAsync(conn, attachmentId);
    if (currentUser is null || attachment is null) return Results.NotFound();
    if (attachment.OwnerUserId != currentUser.Id && !currentUser.CanManageAll()) return Results.Forbid();

    if (File.Exists(attachment.StoragePath)) File.Delete(attachment.StoragePath);
    await using var cmd = new NpgsqlCommand("delete from attachments where id = @id", conn);
    cmd.Parameters.AddWithValue("id", attachmentId);
    await cmd.ExecuteNonQueryAsync();
    await AuditAsync(conn, currentUser.Id, "attachment.delete", "attachments", attachmentId.ToString(), http.Connection.RemoteIpAddress?.ToString());
    return Results.NoContent();
}).RequireAuthorization();

app.Run();

static async Task<UserRecord?> FindUserByEmailAsync(NpgsqlConnection conn, string email)
{
    await using var cmd = new NpgsqlCommand("""
        select id, email, display_name, role, division, unit, password_hash, password_salt, password_iterations, is_active, created_at, updated_at
        from users
        where lower(email) = lower(@email)
        """, conn);
    cmd.Parameters.AddWithValue("email", email.Trim());
    await using var reader = await cmd.ExecuteReaderAsync();
    return await reader.ReadAsync() ? ReadUser(reader) : null;
}

static async Task<UserRecord?> FindUserByIdAsync(NpgsqlConnection conn, Guid id)
{
    await using var cmd = new NpgsqlCommand("""
        select id, email, display_name, role, division, unit, password_hash, password_salt, password_iterations, is_active, created_at, updated_at
        from users
        where id = @id
        """, conn);
    cmd.Parameters.AddWithValue("id", id);
    await using var reader = await cmd.ExecuteReaderAsync();
    return await reader.ReadAsync() ? ReadUser(reader) : null;
}

static async Task<AttachmentRecord?> FindAttachmentAsync(NpgsqlConnection conn, Guid id)
{
    await using var cmd = new NpgsqlCommand("""
        select id, owner_user_id, original_file_name, content_type, size_bytes, storage_path
        from attachments
        where id = @id
        """, conn);
    cmd.Parameters.AddWithValue("id", id);
    await using var reader = await cmd.ExecuteReaderAsync();
    return await reader.ReadAsync()
        ? new AttachmentRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetString(5))
        : null;
}

static UserRecord ReadUser(IDataRecord reader) => new(
    reader.GetGuid(0),
    reader.GetString(1),
    reader.GetString(2),
    reader.GetString(3),
    reader.IsDBNull(4) ? null : reader.GetString(4),
    reader.IsDBNull(5) ? null : reader.GetString(5),
    (byte[])reader[6],
    (byte[])reader[7],
    reader.GetInt32(8),
    reader.GetBoolean(9),
    reader.GetDateTime(10),
    reader.GetDateTime(11));

static UserResponse ToUserResponse(UserRecord user) => new(
    user.Id,
    user.Email,
    user.DisplayName,
    user.Role,
    user.Division,
    user.Unit,
    user.IsActive,
    user.CreatedAt,
    user.UpdatedAt);

static async Task AuditAsync(NpgsqlConnection conn, Guid? userId, string action, string entityType, string entityId, string? ipAddress, NpgsqlTransaction? tx = null)
{
    await using var cmd = new NpgsqlCommand("""
        insert into audit_logs (user_id, action, entity_type, entity_id, ip_address)
        values (@userId, @action, @entityType, @entityId, @ipAddress)
        """, conn, tx);
    cmd.Parameters.AddWithValue("userId", userId.HasValue ? userId.Value : DBNull.Value);
    cmd.Parameters.AddWithValue("action", action);
    cmd.Parameters.AddWithValue("entityType", entityType);
    cmd.Parameters.AddWithValue("entityId", entityId);
    cmd.Parameters.AddWithValue("ipAddress", ipAddress ?? "");
    await cmd.ExecuteNonQueryAsync();
}

static class Roles
{
    public const string SystemAdmin = "systemAdmin";
    public static readonly HashSet<string> DirectorLevel = ["divisionalHead", "director", "secretariat", "deputyDirectorGeneral"];
    public static readonly HashSet<string> ManagerAssignable = ["employee", "unitLead", "divisionalHead", "director", "secretariat", "deputyDirectorGeneral"];
}

static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal principal) =>
        Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Missing user id claim."));
}

static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    public static PasswordHash Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return new PasswordHash(hash, salt, Iterations);
    }

    public static bool Verify(string password, byte[] expectedHash, byte[] salt, int iterations)
    {
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

static class FileName
{
    public static string Sanitize(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "attachment" : cleaned[..Math.Min(cleaned.Length, 160)];
    }
}

record PasswordHash(byte[] Hash, byte[] Salt, int Iterations);

record UserRecord(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    string? Division,
    string? Unit,
    byte[] PasswordHash,
    byte[] PasswordSalt,
    int PasswordIterations,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public bool CanManageAll() => Role == Roles.SystemAdmin;
    public bool CanManageDivision(string division) => CanManageAll() || (Roles.DirectorLevel.Contains(Role) && Division == division);
    public bool CanManageUser(UserRecord target) => CanManageAll() || (target.Division is not null && CanManageDivision(target.Division));
}

record AttachmentRecord(Guid Id, Guid OwnerUserId, string OriginalFileName, string ContentType, long SizeBytes, string StoragePath);

record BootstrapAdminRequest(string BootstrapKey, string Email, string DisplayName, string Password, string? Division, string? Unit);
record RegisterRequest(string Email, string DisplayName, string Password);
record LoginRequest(string Email, string Password);
record UserCreateRequest(string Email, string DisplayName, string Password, string Division, string Unit, string Role);
record AssignmentRequest(string Division, string Unit, string Role);
record SelfProfileRequest(string Division, string Unit);
record UnitCreateRequest(string Name, string Division);
record UserResponse(Guid Id, string Email, string DisplayName, string Role, string? Division, string? Unit, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);
record UnitResponse(Guid Id, string Name, string Division, bool Active);
record AttachmentResponse(Guid Id, string Name, string ContentType, long Size, string DownloadUrl);
