using Npgsql;
using Npgsql.NameTranslation;

namespace CSVC.Infrastructure.Persistence;

// Match DBML labels such as ACTIVE and NO_CHANGE without coupling Core to Npgsql.
internal sealed class DatabaseEnumNameTranslator : INpgsqlNameTranslator
{
    private readonly NpgsqlSnakeCaseNameTranslator _snakeCase = new();

    public string TranslateTypeName(string clrName) => _snakeCase.TranslateTypeName(clrName);

    public string TranslateMemberName(string clrName)
        => _snakeCase.TranslateMemberName(clrName).ToUpperInvariant();
}
