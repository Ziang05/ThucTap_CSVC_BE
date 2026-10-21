using System.Text;
using Npgsql;

namespace CSVC.API.Tests;

internal static class PostgreSqlConstraintChecks
{
    public static async Task VerifyAsync(string connectionString, Guid departmentId, Guid accountId, Guid rootId, Guid childId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var profileId = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO user_profiles(id,department_id,full_name) VALUES (@id,@department,'Second user')",
            ("id", profileId), ("department", departmentId));
        await RejectAsync(connection,
            "INSERT INTO accounts(id,account_code,user_profile_id,role_id,email,password_hash) VALUES (@id,'DUP',@profile,1,' USER@EXAMPLE.COM ','hash')",
            PostgresErrorCodes.UniqueViolation, "uq_accounts_normalized_email", ("id", Guid.NewGuid()), ("profile", profileId));
        await ExecuteAsync(connection, "INSERT INTO departments(id,code,name) VALUES (@id,'PB_UNICODE','Phòng Kỹ thuật')", ("id", Guid.NewGuid()));
        await RejectAsync(connection, "INSERT INTO departments(id,code,name,status) VALUES (@id,'PB_DUP',@name,'LOCKED')",
            PostgresErrorCodes.UniqueViolation, "uq_departments_normalized_name",
            ("id", Guid.NewGuid()), ("name", "  PHÒNG\tKỸ   THUẬT ".Normalize(NormalizationForm.FormD)));
        await RejectAsync(connection, "INSERT INTO departments(id,code,name) VALUES (@id,'PB_EMPTY',E' \t ')",
            PostgresErrorCodes.CheckViolation, "ck_departments_name", ("id", Guid.NewGuid()));
        await RejectAsync(connection, "INSERT INTO areas(id,code,name) VALUES (@id,'ROOT_DUP',' BUILDING   a ')",
            PostgresErrorCodes.UniqueViolation, "uq_areas_root_normalized_name", ("id", Guid.NewGuid()));
        await RejectAsync(connection, "INSERT INTO areas(id,code,name,parent_id) VALUES (@id,'CHILD_DUP',E' FLOOR\t1 ',@parent)",
            PostgresErrorCodes.UniqueViolation, "uq_areas_sibling_normalized_name", ("id", Guid.NewGuid()), ("parent", rootId));
        var otherRoot = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO areas(id,code,name) VALUES (@id,'OTHER_ROOT','Building B')", ("id", otherRoot));
        await ExecuteAsync(connection, "INSERT INTO areas(id,code,name,parent_id) VALUES (@id,'OTHER_CHILD','Floor 1',@parent)",
            ("id", Guid.NewGuid()), ("parent", otherRoot));
        await RejectAsync(connection, "UPDATE areas SET parent_id=id WHERE id=@id",
            PostgresErrorCodes.CheckViolation, "ck_areas_not_self_parent", ("id", rootId));
        await RejectAsync(connection, "UPDATE areas SET parent_id=@child WHERE id=@root",
            PostgresErrorCodes.CheckViolation, "ck_areas_no_cycle", ("root", rootId), ("child", childId));
        await RejectAsync(connection,
            "INSERT INTO auth_sessions(id,account_id,session_token_hash,expires_at) VALUES (@id,@account,'second-active',now()-interval '1 day')",
            PostgresErrorCodes.UniqueViolation, "uq_auth_sessions_active_account", ("id", Guid.NewGuid()), ("account", accountId));
        var resetId = Guid.NewGuid();
        await ExecuteAsync(connection,
            "INSERT INTO password_reset_requests(id,account_id,token_hash,expires_at) VALUES (@id,@account,'reset-first',now()-interval '1 day')",
            ("id", resetId), ("account", accountId));
        await RejectAsync(connection,
            "INSERT INTO password_reset_requests(id,account_id,token_hash,expires_at) VALUES (@id,@account,'reset-second',now()+interval '1 hour')",
            PostgresErrorCodes.UniqueViolation, "uq_password_reset_requests_open_account", ("id", Guid.NewGuid()), ("account", accountId));
        await ExecuteAsync(connection, "UPDATE password_reset_requests SET is_closed=true WHERE id=@id", ("id", resetId));
        await ExecuteAsync(connection,
            "INSERT INTO password_reset_requests(id,account_id,token_hash,expires_at) VALUES (@id,@account,'reset-second',now()+interval '1 hour')",
            ("id", Guid.NewGuid()), ("account", accountId));
        await RejectAsync(connection, "INSERT INTO auth_login_throttles(email_key,failed_count) VALUES ('user@example.com',-1)",
            PostgresErrorCodes.CheckViolation, "ck_auth_login_throttles_failed_count");

        var secondAccount = Guid.NewGuid();
        await ExecuteAsync(connection,
            "INSERT INTO accounts(id,account_code,user_profile_id,role_id,email,password_hash) VALUES (@id,'SECOND',@profile,1,'second@example.com','hash')",
            ("id", secondAccount), ("profile", profileId));
        await VerifyConcurrentSessionsAsync(connectionString, connection, secondAccount);
        await VerifyConcurrentTreeAsync(connectionString, connection);
    }

    private static async Task VerifyConcurrentSessionsAsync(string connectionString, NpgsqlConnection monitor, Guid accountId)
    {
        await using var first = new NpgsqlConnection(connectionString);
        await using var second = new NpgsqlConnection(connectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var transaction = await first.BeginTransactionAsync();
        const string sql = "INSERT INTO auth_sessions(id,account_id,session_token_hash,expires_at) VALUES (@id,@account,@hash,now()+interval '1 hour')";
        await ExecuteAsync(first, sql, ("id", Guid.NewGuid()), ("account", accountId), ("hash", "race-one"));
        var waiting = ExecuteAsync(second, sql, ("id", Guid.NewGuid()), ("account", accountId), ("hash", "race-two"));
        await WaitUntilBlockedAsync(monitor, second.ProcessID);
        await transaction.CommitAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => waiting);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("uq_auth_sessions_active_account", error.ConstraintName);
    }

    private static async Task VerifyConcurrentTreeAsync(string connectionString, NpgsqlConnection monitor)
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await ExecuteAsync(monitor,
            "INSERT INTO areas(id,code,name) VALUES (@first,'RACE_A','Race A'),(@second,'RACE_B','Race B')",
            ("first", firstId), ("second", secondId));
        await using var first = new NpgsqlConnection(connectionString);
        await using var second = new NpgsqlConnection(connectionString);
        await first.OpenAsync();
        await second.OpenAsync();
        await using var transaction = await first.BeginTransactionAsync();
        const string sql = "UPDATE areas SET parent_id=@parent WHERE id=@id";
        await ExecuteAsync(first, sql, ("id", firstId), ("parent", secondId));
        var waiting = ExecuteAsync(second, sql, ("id", secondId), ("parent", firstId));
        await WaitUntilBlockedAsync(monitor, second.ProcessID);
        await transaction.CommitAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(() => waiting);
        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_areas_no_cycle", error.ConstraintName);
    }

    private static async Task WaitUntilBlockedAsync(NpgsqlConnection monitor, int processId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid=@pid AND wait_event_type='Lock')", monitor);
            command.Parameters.AddWithValue("pid", processId);
            if ((bool)(await command.ExecuteScalarAsync())!)
            {
                return;
            }
            await Task.Delay(20);
        }
        Assert.Fail("The concurrent writer did not wait for the first transaction.");
    }

    private static async Task RejectAsync(NpgsqlConnection connection, string sql, string sqlState, string constraint,
        params (string Name, object Value)[] parameters)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, sql, parameters));
        Assert.Equal(sqlState, error.SqlState);
        Assert.Equal(constraint, error.ConstraintName);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 10 };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync();
    }
}
