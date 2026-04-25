using Dapper;

namespace MotorcycleRAG.Persistence.Sql.Repositories;

internal static class ParameterMergeExtensions {
    public static object Merge(this object first, object? second) {
        if (second == null) {
            return first;
        }

        var values = new Dictionary<string, object?>();
        foreach (var property in first.GetType().GetProperties()) {
            values[property.Name] = property.GetValue(first);
        }

        foreach (var property in second.GetType().GetProperties()) {
            values[property.Name] = property.GetValue(second);
        }

        return new DynamicParameters(values);
    }
}