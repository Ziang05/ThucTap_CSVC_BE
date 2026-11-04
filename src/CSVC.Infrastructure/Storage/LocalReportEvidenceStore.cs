using CSVC.UseCases.DamageReports;
using CSVC.UseCases.Exceptions;
using Microsoft.Extensions.Configuration;

namespace CSVC.Infrastructure.Storage;

public sealed class LocalReportEvidenceStore(IConfiguration configuration) : IReportEvidenceStore
{
    private readonly string _root = Path.GetFullPath(configuration["DamageReports:EvidenceRoot"]
        ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "damage-report-evidence"));

    public async Task<string> StoreAsync(ValidatedReportImage image, CancellationToken ct)
    {
        var key = Guid.NewGuid().ToString("N");
        var path = ObjectPath(key); var temporary = path + ".staging";
        try
        {
            Directory.CreateDirectory(_root);
            await File.WriteAllBytesAsync(temporary, image.Bytes, ct);
            File.Move(temporary, path);
            return key;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new UseCaseException(FailureKind.Unavailable, "EVIDENCE_STORAGE_UNAVAILABLE", "Evidence could not be stored.");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        try { return await File.ReadAllBytesAsync(ObjectPath(key), ct); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new UseCaseException(FailureKind.Unavailable, "EVIDENCE_STORAGE_UNAVAILABLE", "Evidence is temporarily unavailable."); }
    }
    public Task DeleteUncommittedAsync(string key, CancellationToken ct)
    {
        try { File.Delete(ObjectPath(key)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Safe orphan: it has no public route without committed metadata. */ }
        return Task.CompletedTask;
    }
    private string ObjectPath(string key)
    {
        if (!Guid.TryParseExact(key, "N", out _)) throw new InvalidOperationException("Invalid evidence object key.");
        return Path.Combine(_root, key);
    }
}
