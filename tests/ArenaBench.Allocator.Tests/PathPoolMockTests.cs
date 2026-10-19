using ArenaBench.Allocator.Core.Solver;

namespace ArenaBench.Allocator.Tests;

/// <summary>
/// <see cref="PathPoolMock"/> stands in for netgraph-optimizer's PathPool in
/// E03/E05, so its semantics have to be the pool semantics and not "some bag": a
/// rent is satisfied by a pooled array when one fits, and only otherwise by a new
/// allocation. The allocation counters exist so the tests can hold it to that.
/// </summary>
public class PathPoolMockTests
{
    [Fact]
    public void Rent_ReturnsAnArrayOfAtLeastTheRequestedSize()
    {
        var pool = new PathPoolMock();

        Assert.True(pool.Rent(4).Length >= 4);
        Assert.True(pool.RentFloat(16).Length >= 16);
        Assert.Equal(1, pool.IntArrayAllocations);
        Assert.Equal(1, pool.FloatArrayAllocations);
    }

    [Fact]
    public void Rent_ReusesAReturnedArray()
    {
        var pool = new PathPoolMock();
        int[] rented = pool.Rent(8);
        pool.Return(rented);

        int[] again = pool.Rent(8);

        Assert.Same(rented, again);
        Assert.Equal(1, pool.IntArrayAllocations);
    }

    [Fact]
    public void Rent_AllocatesWhenThePooledArrayIsTooSmall()
    {
        var pool = new PathPoolMock();
        int[] small = pool.Rent(4);
        pool.Return(small);

        int[] large = pool.Rent(64);

        Assert.True(large.Length >= 64);
        Assert.NotSame(small, large);
        Assert.Equal(2, pool.IntArrayAllocations);

        // The array that was too small stays available for a smaller request.
        Assert.Same(small, pool.Rent(4));
    }

    [Fact]
    public void RentFloat_UsesItsOwnPool()
    {
        var pool = new PathPoolMock();
        float[] floats = pool.RentFloat(8);

        Assert.Equal(0, pool.IntArrayAllocations);
        Assert.Equal(1, pool.FloatArrayAllocations);
        Assert.True(floats.Length >= 8);
        Assert.Equal(0, pool.IntPoolSize);

        pool.ReturnFloat(floats);
        Assert.Equal(1, pool.FloatPoolSize);
    }

    [Fact]
    public void Return_PutsTheArrayBack()
    {
        var pool = new PathPoolMock();
        int[] rented = pool.Rent(8);
        Assert.Equal(0, pool.IntPoolSize);

        pool.Return(rented);
        Assert.Equal(1, pool.IntPoolSize);

        pool.ReturnFloat(pool.RentFloat(8));
        Assert.Equal(1, pool.FloatPoolSize);
    }

    [Fact]
    public void Rent_RejectsANegativeSize()
    {
        var pool = new PathPoolMock();

        Assert.Throws<ArgumentOutOfRangeException>(() => pool.Rent(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.RentFloat(-1));
    }

    [Fact]
    public void Return_RejectsNull()
    {
        var pool = new PathPoolMock();

        Assert.Throws<ArgumentNullException>(() => pool.Return(null!));
        Assert.Throws<ArgumentNullException>(() => pool.ReturnFloat(null!));
    }
}
