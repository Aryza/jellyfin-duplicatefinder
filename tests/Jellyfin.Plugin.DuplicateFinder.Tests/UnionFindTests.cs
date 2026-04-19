using Jellyfin.Plugin.DuplicateFinder.Detection;
using Xunit;

namespace Jellyfin.Plugin.DuplicateFinder.Tests;

/// <summary>
/// The union-find is what lets us merge a chain like A≡B and B≡C into a
/// single three-item group rather than two overlapping pairs. These tests
/// exercise the basic set operations and the transitive grouping guarantee.
/// </summary>
public class UnionFindTests
{
    [Fact]
    public void FindAfterMakeSetReturnsSelf()
    {
        var uf = new UnionFind<int>();
        uf.MakeSet(1);
        Assert.Equal(1, uf.Find(1));
    }

    [Fact]
    public void UnionJoinsTwoSingletons()
    {
        var uf = new UnionFind<int>();
        uf.MakeSet(1);
        uf.MakeSet(2);
        uf.Union(1, 2);
        Assert.Equal(uf.Find(1), uf.Find(2));
    }

    [Fact]
    public void TransitiveUnionProducesSingleRoot()
    {
        var uf = new UnionFind<int>();
        for (int i = 1; i <= 5; i++) uf.MakeSet(i);

        uf.Union(1, 2);
        uf.Union(3, 4);
        uf.Union(2, 3); // bridges {1,2} and {3,4}

        var root1 = uf.Find(1);
        Assert.Equal(root1, uf.Find(2));
        Assert.Equal(root1, uf.Find(3));
        Assert.Equal(root1, uf.Find(4));
        Assert.NotEqual(root1, uf.Find(5)); // 5 is still alone
    }

    [Fact]
    public void DisjointSetsStaySeparate()
    {
        var uf = new UnionFind<string>();
        uf.MakeSet("a"); uf.MakeSet("b"); uf.MakeSet("c");
        uf.Union("a", "b");
        Assert.NotEqual(uf.Find("a"), uf.Find("c"));
        Assert.NotEqual(uf.Find("b"), uf.Find("c"));
    }

    [Fact]
    public void UnionOfAlreadyConnectedElementsIsANoOp()
    {
        var uf = new UnionFind<int>();
        uf.MakeSet(1); uf.MakeSet(2); uf.MakeSet(3);
        uf.Union(1, 2);
        uf.Union(2, 3);

        var root = uf.Find(1);
        uf.Union(1, 3); // already in same set
        Assert.Equal(root, uf.Find(1));
        Assert.Equal(root, uf.Find(3));
    }

    [Fact]
    public void DuplicateMakeSetIsIgnored()
    {
        var uf = new UnionFind<int>();
        uf.MakeSet(1);
        uf.MakeSet(1); // should not reset the parent
        Assert.Equal(1, uf.Find(1));
    }

    [Fact]
    public void UsesGuidKeys()
    {
        // Sanity check that the generic parameter works with Guid (the real
        // production key type). The awkwardly-named `UnionFind<Guid>` generic
        // shadows the Guid type inside its body, but externally we can still
        // pass System.Guid values.
        var uf = new UnionFind<System.Guid>();
        var a = System.Guid.NewGuid();
        var b = System.Guid.NewGuid();
        uf.MakeSet(a); uf.MakeSet(b);
        uf.Union(a, b);
        Assert.Equal(uf.Find(a), uf.Find(b));
    }
}
