using ReignBeta.Shared;

namespace Reign.Mcp.Tests;

public sealed class CorrespondenceContactOrderingTests
{
    private sealed record Contact(string Id, string Name, int Unread, bool History);
    private static List<Contact> Order(IEnumerable<Contact> contacts) => CorrespondenceContactOrdering.Order(
        contacts, row => row.Unread, row => row.History, row => row.Name, row => row.Id).ToList();

    [Fact]
    public void HundredsOfContactsPutUnreadThenAllHistoryAboveNewPeople()
    {
        var rows = Enumerable.Range(0, 600).Select(i => new Contact(i.ToString(), "A" + i.ToString("000"), 0, false)).ToList();
        var old = new Contact("old", "Z old outgoing or read mail", 0, true);
        var unread = new Contact("unread", "Z unread mail", 1, true);
        rows.Insert(300, old); rows.Add(unread);
        var ordered = Order(rows);
        Assert.Same(unread, ordered[0]); Assert.Same(old, ordered[1]);
        Assert.Equal(602, ordered.Select(row => row.Id).Distinct().Count());
        Assert.Equal(rows.Where(row => !row.History).OrderBy(row => row.Name), ordered.Skip(2));
    }

    [Fact]
    public void ReadingLastUnreadMovesContactIntoHistoryWhilePreservingIdentity()
    {
        var read = new Contact("read", "Alice", 0, true);
        var selected = new Contact("selected", "Zoe", 1, true);
        var otherUnread = new Contact("other", "Bea", 3, true);
        Assert.Equal(new[] { otherUnread, selected, read }, Order(new[] { read, selected, otherUnread }));
        selected = selected with { Unread = 0 };
        Assert.Equal(new[] { otherUnread, read, selected }, Order(new[] { read, selected, otherUnread }));
    }

    [Fact]
    public void EqualNamesHaveStableIdentityTieBreakAndUnreadCountDoesNotChangeNameOrder()
    {
        var a = new Contact("a", "Same", 1, true);
        var b = new Contact("b", "same", 50, true);
        Assert.Equal(new[] { a, b }, Order(new[] { b, a }));
        Assert.Equal(new[] { a, b }, Order(new[] { a, b }));
    }
}
