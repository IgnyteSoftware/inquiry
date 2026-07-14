namespace Inquiry.Testing;

/// <summary>
/// Builds test entities from a delegate, with an independent sequence and optional named states.
/// The factory only constructs entities; persistence remains the caller's responsibility.
/// </summary>
public sealed class EntityFactory<TEntity>
    where TEntity : class
{
    private readonly Func<long, TEntity> _create;
    private readonly Dictionary<string, Action<TEntity, long>> _states = new(StringComparer.Ordinal);
    private readonly object _statesLock = new();
    private long _sequence;

    /// <summary>
    /// Creates a factory whose delegate receives a one-based, per-factory sequence number.
    /// </summary>
    public EntityFactory(Func<long, TEntity> create)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
    }

    /// <summary>
    /// Creates a factory from a parameterless delegate. This overload can wrap Bogus or any
    /// other object generator without adding a dependency on that library.
    /// </summary>
    public EntityFactory(Func<TEntity> create)
        : this(create is null
            ? throw new ArgumentNullException(nameof(create))
            : _ => create())
    {
    }

    /// <summary>Defines a named state that can be selected by <see cref="Build"/>.</summary>
    public EntityFactory<TEntity> State(string name, Action<TEntity> apply)
    {
        if (apply is null) throw new ArgumentNullException(nameof(apply));
        return State(name, (entity, _) => apply(entity));
    }

    /// <summary>
    /// Defines a named state whose delegate also receives the entity's sequence number.
    /// </summary>
    public EntityFactory<TEntity> State(string name, Action<TEntity, long> apply)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("State name cannot be empty.", nameof(name));
        if (apply is null) throw new ArgumentNullException(nameof(apply));

        lock (_statesLock)
        {
            if (!_states.TryAdd(name, apply))
            {
                throw new ArgumentException($"A state named '{name}' is already defined.", nameof(name));
            }
        }

        return this;
    }

    /// <summary>
    /// Builds one entity and applies the requested named states in the order supplied.
    /// </summary>
    public TEntity Build(params string[] states)
    {
        if (states is null) throw new ArgumentNullException(nameof(states));

        var actions = new Action<TEntity, long>[states.Length];
        if (states.Length > 0)
        {
            lock (_statesLock)
            {
                for (var i = 0; i < states.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(states[i]))
                    {
                        throw new ArgumentException("State name cannot be empty.", nameof(states));
                    }

                    if (!_states.TryGetValue(states[i], out var action))
                    {
                        throw new ArgumentException($"No state named '{states[i]}' is defined.", nameof(states));
                    }

                    actions[i] = action;
                }
            }
        }

        var sequence = Interlocked.Increment(ref _sequence);
        var entity = _create(sequence);
        for (var i = 0; i < actions.Length; i++)
        {
            actions[i](entity, sequence);
        }

        return entity;
    }

    /// <summary>
    /// Builds a deterministic sequence of entities, applying the same states to each entity.
    /// </summary>
    public IReadOnlyList<TEntity> BuildMany(int count, params string[] states)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (states is null) throw new ArgumentNullException(nameof(states));

        var entities = new TEntity[count];
        for (var i = 0; i < entities.Length; i++)
        {
            entities[i] = Build(states);
        }

        return entities;
    }
}
