using System.Diagnostics.CodeAnalysis;

namespace IkuyoPet.Pet;

public interface IRandomIndexSource
{
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Task API requires IRandomIndexSource.Next(int).")]
    int Next(int exclusiveUpperBound);
}

public sealed class SharedRandomIndexSource : IRandomIndexSource
{
    public int Next(int exclusiveUpperBound) => Random.Shared.Next(exclusiveUpperBound);
}

public sealed class PetInteractionSelector
{
    private readonly PetInteractionCatalog _catalog;
    private readonly IRandomIndexSource _random;
    private int? _previousId;

    public PetInteractionSelector(PetInteractionCatalog catalog, IRandomIndexSource? random = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (catalog.Messages is null || catalog.Messages.Count == 0)
            throw new ArgumentException("Catalog must contain at least one message.", nameof(catalog));

        _catalog = catalog;
        _random = random ?? new SharedRandomIndexSource();
    }

    public PetInteractionMessage Next()
    {
        var candidates = _catalog.Messages.Count > 1 && _previousId.HasValue
            ? _catalog.Messages.Where(message => message.Id != _previousId.Value).ToArray()
            : _catalog.Messages;
        if (candidates.Count == 0)
            candidates = _catalog.Messages;

        var index = _random.Next(candidates.Count);
        if ((uint)index >= (uint)candidates.Count)
            throw new ArgumentOutOfRangeException(nameof(index), "Random source returned an invalid index.");

        var selected = candidates[index];
        _previousId = selected.Id;
        return selected;
    }
}
