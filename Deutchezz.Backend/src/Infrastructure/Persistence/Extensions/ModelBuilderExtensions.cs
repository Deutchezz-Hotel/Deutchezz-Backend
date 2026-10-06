using Microsoft.EntityFrameworkCore;

namespace Deutchezz.Backend.Infrastructure.Persistence.Extensions;

public static class ModelBuilderExtensions
{
    public static void AddSoftDeleteQueryFilter(this Microsoft.EntityFrameworkCore.Metadata.IMutableEntityType entityType)
    {
        var clrType = entityType.ClrType;
        if (!typeof(Domain.Common.SoftDeletableEntity).IsAssignableFrom(clrType))
            return;

        var parameter = System.Linq.Expressions.Expression.Parameter(clrType, "e");
        var property = System.Linq.Expressions.Expression.Property(parameter, nameof(Domain.Common.SoftDeletableEntity.IsDeleted));
        var condition = System.Linq.Expressions.Expression.Equal(property, System.Linq.Expressions.Expression.Constant(false));
        var lambda = System.Linq.Expressions.Expression.Lambda(condition, parameter);

        entityType.SetQueryFilter(lambda);
    }
}