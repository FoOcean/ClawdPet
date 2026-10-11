namespace ClawdPet.Core.Behaviors;

/// <summary>How a behavior is created and when it shows up.</summary>
public sealed record BehaviorInfo(string Name, Func<PetBehavior> Factory)
{
    /// <summary>Label for menus; null keeps it out of the menu.</summary>
    public string? MenuLabel { get; init; }
    /// <summary>Weight for being picked when the pet acts on its own; null/0 = never.</summary>
    public Func<PetContext, double>? AutoWeight { get; init; }
    /// <summary>Extra names accepted by commands.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

public sealed class BehaviorRegistry
{
    readonly Dictionary<string, BehaviorInfo> _byName = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    readonly List<BehaviorInfo> _ordered = [];

    public IReadOnlyList<BehaviorInfo> All => _ordered;

    /// <summary>Adds or replaces a behavior (replacing keeps its menu position).</summary>
    public BehaviorRegistry Register(BehaviorInfo info)
    {
        int at = _ordered.FindIndex(b => string.Equals(b.Name, info.Name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) _ordered[at] = info; else _ordered.Add(info);
        _byName[info.Name] = info;
        foreach (var a in info.Aliases) _aliases[a] = info.Name;
        return this;
    }

    public bool TryGet(string nameOrAlias, out BehaviorInfo info)
    {
        if (_byName.TryGetValue(nameOrAlias, out info!)) return true;
        return _aliases.TryGetValue(nameOrAlias, out var n) && _byName.TryGetValue(n, out info!);
    }

    public PetBehavior Create(BehaviorInfo info)
    {
        var b = info.Factory();
        b.Name = info.Name;
        return b;
    }
}
