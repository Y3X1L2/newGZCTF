using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Infrastructure.Persistence;

internal static class JsonCollectionConversion
{
    public static PropertyBuilder<List<T>> HasJsonListConversion<T>(this PropertyBuilder<List<T>> property)
        where T : notnull
    {
        property.HasConversion(
            value => JsonSerializer.Serialize(value, AppDbContext.JsonOptions),
            value => JsonSerializer.Deserialize<List<T>>(value, AppDbContext.JsonOptions) ?? new());
        property.Metadata.SetValueComparer(new ValueComparer<List<T>>(
            (left, right) => ReferenceEquals(left, right) ||
                             left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            value => value.ToList()));
        return property;
    }
}
