using IkuyoPet.Pet;
using Xunit;

namespace IkuyoPet.Infrastructure.Tests.Pet;

public sealed class PetInteractionSelectorTests
{
    [Fact]
    public void MultipleMessagesRememberPreviousIdAndAvoidConsecutiveRepeats()
    {
        var catalog = Catalog(
            new PetInteractionMessage(1, "one", null),
            new PetInteractionMessage(2, "two", null),
            new PetInteractionMessage(3, "three", null));
        var random = new SequenceRandomIndexSource(0, 0);
        var selector = new PetInteractionSelector(catalog, random);

        var first = selector.Next();
        var second = selector.Next();

        Assert.Equal(1, first.Id);
        Assert.Equal(2, second.Id);
        Assert.Equal([3, 2], random.ExclusiveUpperBounds);
    }

    [Fact]
    public void SingleMessageCanRepeatPreviousId()
    {
        var catalog = Catalog(new PetInteractionMessage(7, "only", null));
        var selector = new PetInteractionSelector(catalog, new SequenceRandomIndexSource(0, 0));

        var first = selector.Next();
        var second = selector.Next();

        Assert.Equal(7, first.Id);
        Assert.Equal(7, second.Id);
    }

    [Fact]
    public void InvalidRandomIndexThrows()
    {
        var catalog = Catalog(
            new PetInteractionMessage(1, "one", null),
            new PetInteractionMessage(2, "two", null));
        var selector = new PetInteractionSelector(catalog, new SequenceRandomIndexSource(2));

        Assert.Throws<ArgumentOutOfRangeException>(() => selector.Next());
    }

    [Fact]
    public void EmptyCatalogThrows()
    {
        var catalog = new PetInteractionCatalog("default", "click", "zh-CN", []);

        Assert.Throws<ArgumentException>(() => new PetInteractionSelector(catalog));
    }

    private static PetInteractionCatalog Catalog(params PetInteractionMessage[] messages) =>
        new("default", "click", "zh-CN", messages);

    private sealed class SequenceRandomIndexSource(params int[] indexes) : IRandomIndexSource
    {
        private int _position;

        public List<int> ExclusiveUpperBounds { get; } = [];

        public int Next(int exclusiveUpperBound)
        {
            ExclusiveUpperBounds.Add(exclusiveUpperBound);
            return indexes[_position++];
        }
    }
}
