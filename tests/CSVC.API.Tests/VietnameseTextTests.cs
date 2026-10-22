using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using CSVC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CSVC.API.Tests;

public sealed class VietnameseTextTests
{
    private const string Description = "Tiếng Việt: ă â đ ê ô ơ ư; Á À Ả Ã Ạ; Ắ Ằ Ẳ Ẵ Ặ; Ấ Ầ Ẩ Ẫ Ậ; "
        + "É È Ẻ Ẽ Ẹ; Ế Ề Ể Ễ Ệ; Í Ì Ỉ Ĩ Ị; Ó Ò Ỏ Õ Ọ; Ố Ồ Ổ Ỗ Ộ; Ớ Ờ Ở Ỡ Ợ; "
        + "Ú Ù Ủ Ũ Ụ; Ứ Ừ Ử Ữ Ự; Ý Ỳ Ỷ Ỹ Ỵ.\nDữ liệu được giữ nguyên dấu.";

    [PostgreSqlFact]
    public Task Literal_utf8_requests_round_trip_names_descriptions_paths_and_audits()
        => RoundTripAsync(escaped: false);

    [PostgreSqlFact]
    public Task Escaped_unicode_requests_round_trip_names_descriptions_paths_and_audits()
        => RoundTripAsync(escaped: true);

    private static async Task RoundTripAsync(bool escaped)
    {
        await using var database = await AccountTestDatabase.CreateAsync();
        await using var factory = new AccountApiTestHost(database.ConnectionString);
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", seed.ManagerToken);

        const string departmentName = "Phòng Điều phối";
        var department = await CreateAsync(client, "departments", new
        {
            name = Decomposed(departmentName), shortName = "ĐP", description = Description
        }, escaped);
        var departmentId = department.GetProperty("id").GetGuid();
        Assert.Equal(departmentName, department.GetProperty("name").GetString());
        Assert.Equal("ĐP", department.GetProperty("shortName").GetString());
        Assert.Equal(Description, department.GetProperty("description").GetString());
        var departmentDetail = await GetAsync(client, $"departments/{departmentId}");
        Assert.Equal(departmentName, departmentDetail.GetProperty("name").GetString());
        Assert.Equal(Description, departmentDetail.GetProperty("description").GetString());

        const string originalName = "Đặng Thị Hồng Ánh";
        const string updatedName = "Nguyễn Đình Trường";
        var account = await CreateAsync(client, "accounts", new
        {
            fullName = Decomposed(originalName), email = "vietnamese@example.com", roleId = 2, departmentId
        }, escaped);
        var accountId = account.GetProperty("id").GetGuid();
        var detail = await GetAsync(client, $"accounts/{accountId}");
        Assert.Equal(originalName, detail.GetProperty("fullName").GetString());
        Assert.Equal(departmentName, detail.GetProperty("departmentName").GetString());
        using (var body = Content(new { fullName = Decomposed(updatedName) }, escaped))
        using (var response = await client.PutAsync($"/api/v1/accounts/{accountId}/profile", body))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        detail = await GetAsync(client, $"accounts/{accountId}");
        Assert.Equal(updatedName, detail.GetProperty("fullName").GetString());

        using var signedIn = factory.CreateClient();
        using (var body = Content(new { email = "vietnamese@example.com", password = "vietnamese@example.com" }, escaped: false))
        using (var response = await signedIn.PostAsync("/api/v1/auth/login", body))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var login = await ReadAsync(response);
            Assert.Equal(updatedName, login.GetProperty("user").GetProperty("fullName").GetString());
            Assert.Equal(departmentName, login.GetProperty("user").GetProperty("departmentName").GetString());
            signedIn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("accessToken").GetString());
        }
        var ownProfile = await GetAsync(signedIn, "accounts/me");
        Assert.Equal(updatedName, ownProfile.GetProperty("fullName").GetString());

        const string rootName = "Tòa nhà Đông Bắc";
        const string childName = "Phòng Thí nghiệm Hóa học";
        var root = await CreateAsync(client, "areas", new { name = Decomposed(rootName) }, escaped);
        var rootId = root.GetProperty("id").GetGuid();
        var child = await CreateAsync(client, "areas", new
        {
            name = Decomposed(childName), parentId = rootId, typeLabel = "Phòng", description = Description
        }, escaped);
        var childId = child.GetProperty("id").GetGuid();
        child = await GetAsync(client, $"areas/{childId}");
        Assert.Equal(childName, child.GetProperty("name").GetString());
        Assert.Equal("Phòng", child.GetProperty("typeLabel").GetString());
        Assert.Equal(Description, child.GetProperty("description").GetString());
        Assert.Equal(new[] { rootName, childName }, child.GetProperty("pathNames").EnumerateArray().Select(x => x.GetString()));

        // Queries exercise URL decoding and PostgreSQL lower()/NFC behavior, including Đ/đ.
        await AssertSearchAsync(client, "accounts", "NGUYỄN ĐÌNH", accountId);
        await AssertSearchAsync(client, "departments", "ĐIỀU PHỐI", departmentId);
        await AssertSearchAsync(client, $"areas/{rootId}/children", "THÍ NGHIỆM", childId);
        var noAccents = await GetAsync(client, "accounts?search=nguyen");
        Assert.Equal(0, noAccents.GetProperty("totalCount").GetInt32());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CsvcDbContext>();
        var storedDepartment = await db.Departments.AsNoTracking().SingleAsync(d => d.Id == departmentId);
        Assert.Equal(departmentName, storedDepartment.Name);
        Assert.Equal(Description, storedDepartment.Description);
        var profileId = await db.Accounts.Where(a => a.Id == accountId).Select(a => a.UserProfileId).SingleAsync();
        Assert.Equal(updatedName, (await db.UserProfiles.AsNoTracking().SingleAsync(p => p.Id == profileId)).FullName);
        var storedArea = await db.Areas.AsNoTracking().SingleAsync(a => a.Id == childId);
        Assert.Equal(childName, storedArea.Name);
        Assert.Equal(Description, storedArea.Description);
        var departmentAudit = await db.DepartmentHistories.SingleAsync(h => h.DepartmentId == departmentId && h.ActionCode == "DEPARTMENT_CREATED");
        Assert.Equal(departmentName, Snapshot(departmentAudit.AfterData!, "name"));
        Assert.Equal(Description, Snapshot(departmentAudit.AfterData!, "description"));
        var accountAudit = await db.AccountHistories.SingleAsync(h => h.TargetAccountId == accountId && h.ActionCode == "PROFILE_UPDATED");
        Assert.Equal(originalName, Snapshot(accountAudit.BeforeData!, "full_name"));
        Assert.Equal(updatedName, Snapshot(accountAudit.AfterData!, "full_name"));
        var areaAudit = await db.AreaHistories.SingleAsync(h => h.AreaId == childId && h.ActionCode == "AREA_CREATED");
        Assert.Equal(childName, Snapshot(areaAudit.AfterData!, "name"));
        Assert.Equal(Description, Snapshot(areaAudit.AfterData!, "description"));
    }

    private static string Decomposed(string value) => $"  {value.Replace(" ", "\t  ")}  ".Normalize(NormalizationForm.FormD);

    private static StringContent Content(object value, bool escaped)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        if (!escaped) options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
        var json = JsonSerializer.Serialize(value, options);
        Assert.Equal(escaped, json.Contains("\\u", StringComparison.Ordinal));
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string path, object value, bool escaped)
    {
        using var body = Content(value, escaped);
        using var response = await client.PostAsync($"/api/v1/{path}", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync($"/api/v1/{path}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync(response);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        var json = new UTF8Encoding(false, true).GetString(await response.Content.ReadAsByteArrayAsync());
        Assert.DoesNotContain('\uFFFD', json);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task AssertSearchAsync(HttpClient client, string path, string search, Guid expectedId)
    {
        var list = await GetAsync(client, $"{path}?search={Uri.EscapeDataString(search.Normalize(NormalizationForm.FormD))}");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(expectedId, list.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    private static string? Snapshot(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetString();
    }
}
