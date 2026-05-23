using System.Collections.ObjectModel;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Inquiry;

internal static class ReflectionInquiryEntityDescriptor
{
    public static object Create(Type entityType, InquiryConventionOptions conventions)
    {
        var method = typeof(ReflectionInquiryEntityDescriptor<>)
            .MakeGenericType(entityType)
            .GetMethod("CreateCore", BindingFlags.NonPublic | BindingFlags.Static)!;

        try
        {
            return method.Invoke(null, new object[] { conventions })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }
}

internal sealed class ReflectionInquiryEntityDescriptor<TEntity> :
    IInquiryEntityDescriptor<TEntity>,
    IInquiryMaterializer<TEntity>
{
    private ReflectionInquiryEntityDescriptor(
        string tableName,
        string? schema,
        IReadOnlyList<IInquiryPropertyDescriptor<TEntity>> properties,
        IReadOnlyList<IInquiryPropertyDescriptor<TEntity>> keys,
        IInquiryPropertyDescriptor<TEntity>? concurrencyToken)
    {
        TableName = tableName;
        Schema = schema;
        Properties = properties;
        Keys = keys;
        ConcurrencyToken = concurrencyToken;
    }

    public string TableName { get; }

    public string? Schema { get; }

    public IReadOnlyList<IInquiryPropertyDescriptor<TEntity>> Properties { get; }

    public IReadOnlyList<IInquiryPropertyDescriptor<TEntity>> Keys { get; }

    public IInquiryPropertyDescriptor<TEntity>? ConcurrencyToken { get; }

    public static ReflectionInquiryEntityDescriptor<TEntity> Create(InquiryConventionOptions conventions)
    {
        return (ReflectionInquiryEntityDescriptor<TEntity>)global::Inquiry.ReflectionInquiryEntityDescriptor.Create(typeof(TEntity), conventions);
    }

    public TEntity Materialize(DbDataReader reader)
    {
        var entity = CreateEntity();

        for (var index = 0; index < Properties.Count; index++)
        {
            var property = Properties[index];
            var value = InquiryValueConverter.FromDatabaseValue(reader.IsDBNull(index) ? null : reader.GetValue(index), property.PropertyType);
            property.SetValue(entity, value);
        }

        return entity;
    }

    private static ReflectionInquiryEntityDescriptor<TEntity> CreateCore(InquiryConventionOptions conventions)
    {
        var entityType = typeof(TEntity);
        var table = entityType.GetCustomAttribute<InquiryTableAttribute>();
        if (table is null && !conventions.AllowUnattributedEntities)
        {
            throw new InquiryMappingException(
                $"Entity type '{entityType.FullName}' must be annotated with [InquiryTable] or conventions must allow unattributed entities.");
        }

        var tableName = table?.Name ?? conventions.ConvertTableName(entityType.Name);
        var schema = table?.Schema ?? conventions.DefaultSchema;
        var mappedProperties = new List<IInquiryPropertyDescriptor<TEntity>>();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IInquiryPropertyDescriptor<TEntity>? concurrencyToken = null;

        foreach (var property in entityType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var ignore = property.GetCustomAttribute<InquiryIgnoreAttribute>() is not null;
            var column = property.GetCustomAttribute<InquiryColumnAttribute>();
            if (ignore && column is not null)
            {
                throw new InquiryMappingException(
                    $"Property '{entityType.FullName}.{property.Name}' cannot use both [InquiryIgnore] and [InquiryColumn].");
            }

            if (ignore || property.GetMethod is null)
            {
                continue;
            }

            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var columnName = column?.Name ?? conventions.ConvertColumnName(property.Name);
            if (!columns.Add(columnName))
            {
                throw new InquiryMappingException(
                    $"Entity type '{entityType.FullName}' maps more than one property to column '{columnName}'.");
            }

            var key = property.GetCustomAttribute<InquiryKeyAttribute>();
            var isReadOnly = property.GetCustomAttribute<InquiryReadOnlyAttribute>() is not null ||
                             property.GetCustomAttribute<InquiryComputedAttribute>() is not null;
            var isConcurrencyToken = property.GetCustomAttribute<InquiryConcurrencyTokenAttribute>() is not null;
            var isInsertable = !isReadOnly &&
                               property.GetCustomAttribute<InquiryInsertIgnoreAttribute>() is null &&
                               !(key?.DatabaseGenerated ?? false);
            var isUpdateable = !isReadOnly &&
                               !isConcurrencyToken &&
                               key is null &&
                               property.GetCustomAttribute<InquiryUpdateIgnoreAttribute>() is null;

            var descriptor = new InquiryPropertyDescriptor<TEntity>(
                property.Name,
                columnName,
                property.PropertyType,
                key is not null,
                key?.DatabaseGenerated ?? false,
                isInsertable,
                isUpdateable,
                CreateGetter(property),
                CreateSetter(property));

            if (isConcurrencyToken)
            {
                if (concurrencyToken is not null)
                {
                    throw new InquiryMappingException(
                        $"Entity type '{entityType.FullName}' can only declare one concurrency token.");
                }

                concurrencyToken = descriptor;
            }

            mappedProperties.Add(descriptor);
        }

        if (mappedProperties.Count == 0)
        {
            throw new InquiryMappingException($"Entity type '{entityType.FullName}' does not contain mapped properties.");
        }

        var keys = mappedProperties
            .Where(property => property.IsKey)
            .OrderBy(property => GetKeyOrder(entityType, property.PropertyName))
            .ThenBy(property => property.PropertyName, StringComparer.Ordinal)
            .ToArray();

        var duplicateOrders = keys
            .Select(property => GetKeyOrder(entityType, property.PropertyName))
            .GroupBy(order => order)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateOrders.Length > 0)
        {
            throw new InquiryMappingException(
                $"Entity type '{entityType.FullName}' has duplicate composite key order value(s): {string.Join(", ", duplicateOrders)}.");
        }

        return new ReflectionInquiryEntityDescriptor<TEntity>(
            tableName,
            schema,
            new ReadOnlyCollection<IInquiryPropertyDescriptor<TEntity>>(mappedProperties),
            new ReadOnlyCollection<IInquiryPropertyDescriptor<TEntity>>(keys),
            concurrencyToken);
    }

    private static TEntity CreateEntity()
    {
        try
        {
            return Activator.CreateInstance<TEntity>();
        }
        catch (Exception ex)
        {
            throw new InquiryMappingException(
                $"Entity type '{typeof(TEntity).FullName}' must have a public parameterless constructor for reflection materialization.",
                ex);
        }
    }

    private static int GetKeyOrder(Type entityType, string propertyName)
    {
        return entityType.GetProperty(propertyName)!.GetCustomAttribute<InquiryKeyAttribute>()?.Order ?? 0;
    }

    private static Func<TEntity, object?> CreateGetter(PropertyInfo property)
    {
        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var access = Expression.Property(entity, property);
        var convert = Expression.Convert(access, typeof(object));
        return Expression.Lambda<Func<TEntity, object?>>(convert, entity).Compile();
    }

    private static Action<TEntity, object?> CreateSetter(PropertyInfo property)
    {
        if (property.SetMethod is null || !property.SetMethod.IsPublic)
        {
            return (_, _) => throw new InquiryMappingException(
                $"Property '{typeof(TEntity).FullName}.{property.Name}' must have a public setter for materialization.");
        }

        var entity = Expression.Parameter(typeof(TEntity), "entity");
        var value = Expression.Parameter(typeof(object), "value");
        var converted = Expression.Call(
            typeof(InquiryValueConverter),
            nameof(InquiryValueConverter.FromDatabaseValue),
            Type.EmptyTypes,
            value,
            Expression.Constant(property.PropertyType));
        var assignment = Expression.Assign(Expression.Property(entity, property), Expression.Convert(converted, property.PropertyType));
        return Expression.Lambda<Action<TEntity, object?>>(assignment, entity, value).Compile();
    }
}
