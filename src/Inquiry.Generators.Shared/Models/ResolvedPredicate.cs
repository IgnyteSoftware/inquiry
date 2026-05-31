using Inquiry.Generators.Abstractions;
using System.Collections.Generic;

namespace Inquiry.Generators.Models;

/// <summary>
/// One bound parameter of a resolved predicate: the SQL parameter name, the method parameter it reads
/// from, the column it filters, and whether it is an <c>IN</c> collection (which the runtime expands).
/// Emit-time only — never part of the cached model.
/// </summary>
internal readonly struct PredicateBinding
{
    public PredicateBinding(string sqlParameterName, int methodParameterIndex, ColumnData column, bool isCollection)
    {
        SqlParameterName = sqlParameterName;
        MethodParameterIndex = methodParameterIndex;
        Column = column;
        IsCollection = isCollection;
    }

    public string SqlParameterName { get; }
    public int MethodParameterIndex { get; }
    public ColumnData Column { get; }
    public bool IsCollection { get; }
}

/// <summary>
/// A <c>SelectAllByPredicate</c> method's criteria resolved against the entity: the
/// <see cref="SqlPredicate"/> list handed to the <see cref="SqlBuilder"/> and the parallel parameter
/// bindings the emitter writes into the command. <see cref="HasIn"/> is true when any criterion uses
/// <c>IN</c>, which routes the method through the runtime command-text-rewrite path.
/// </summary>
internal sealed class ResolvedPredicatePlan
{
    public ResolvedPredicatePlan(IReadOnlyList<SqlPredicate> predicates, IReadOnlyList<PredicateBinding> bindings, bool hasIn)
    {
        Predicates = predicates;
        Bindings = bindings;
        HasIn = hasIn;
    }

    public IReadOnlyList<SqlPredicate> Predicates { get; }
    public IReadOnlyList<PredicateBinding> Bindings { get; }
    public bool HasIn { get; }
}
