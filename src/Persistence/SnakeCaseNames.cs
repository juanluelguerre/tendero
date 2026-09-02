using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ElGuerre.Tendero.Persistence;

/// <summary>
/// snake_case names without adding a package for it: tables, columns and indexes
/// read without quotes in psql, which is where debugging actually happens.
/// Inside the JSON columns the convention is camelCase, the same one
/// <see cref="Jsonb"/>'s converters use, so that all the database's JSON looks
/// the same wherever it came from.
/// </summary>
internal static class SnakeCaseNames
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var tableName = entity.GetTableName();
            if (tableName is not null)
                entity.SetTableName(ToSnakeCase(tableName));

            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));

            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName()!));

            foreach (var index in entity.GetIndexes())
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));

            foreach (var foreignKey in entity.GetForeignKeys())
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));

            foreach (var complex in entity.GetComplexProperties())
                ApplyToComplexType(complex.ComplexType);
        }
    }

    private static void ApplyToComplexType(IMutableComplexType complexType)
    {
        var insideJson = complexType.IsMappedToJson();

        foreach (var property in complexType.GetProperties())
        {
            if (insideJson)
                property.SetJsonPropertyName(ToCamelCase(property.Name));
            else
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
        }

        foreach (var nested in complexType.GetComplexProperties())
            ApplyToComplexType(nested.ComplexType);
    }

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : string.Create(name.Length, name, static (span, value) =>
        {
            value.CopyTo(span);
            span[0] = char.ToLowerInvariant(span[0]);
        });

    /// <summary>
    /// An underscore before every capital that starts a word. The condition at
    /// the end is what keeps "IX_Orders" from becoming "i_x_orders".
    /// </summary>
    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var current = name[index];

            if (char.IsUpper(current) && index > 0 && name[index - 1] != '_')
            {
                var startsWord = !char.IsUpper(name[index - 1])
                                 || (index + 1 < name.Length && char.IsLower(name[index + 1]));

                if (startsWord)
                    builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}
