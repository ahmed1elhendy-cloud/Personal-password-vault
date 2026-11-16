using PasswordVault.Core;
using Xunit;

namespace PasswordVault.Core.Tests;

public sealed class MergeTests
{
    private static readonly DateTimeOffset BaseTime = DateTimeOffset.Parse("2026-03-01T00:00:00+00:00");

    [Fact]
    public void MergeNewerEditWinsInBothDirections()
    {
        var id = Guid.NewGuid();
        var older = CreateEntry(id, "Older", 1);
        var newer = CreateEntry(id, "Newer", 2);

        var localNewer = VaultMerger.Merge(Data(older), Data(newer));
        var remoteNewer = VaultMerger.Merge(Data(newer), Data(older));

        Assert.Equal("Newer", Assert.Single(localNewer.Entries).Title);
        Assert.Equal("Newer", Assert.Single(remoteNewer.Entries).Title);
    }

    [Fact]
    public void MergePhoneEditsThenPcDeletesLaterDeleteWins()
    {
        var id = Guid.NewGuid();
        var phoneEdit = CreateEntry(id, "Phone edit", 2);
        var pcDelete = CreateEntry(id, "PC delete", 3, isDeleted: true);

        var merged = VaultMerger.Merge(Data(phoneEdit), Data(pcDelete));

        var result = Assert.Single(merged.Entries);
        Assert.True(result.IsDeleted);
        Assert.Equal("PC delete", result.Title);
    }

    [Fact]
    public void MergePcDeletesThenPhoneEditsLaterEditWins()
    {
        var id = Guid.NewGuid();
        var pcDelete = CreateEntry(id, "PC delete", 2, isDeleted: true);
        var phoneEdit = CreateEntry(id, "Phone edit", 3);

        var merged = VaultMerger.Merge(Data(pcDelete), Data(phoneEdit));

        var result = Assert.Single(merged.Entries);
        Assert.False(result.IsDeleted);
        Assert.Equal("Phone edit", result.Title);
    }

    [Fact]
    public void MergeTieWithDeleteDeleteWins()
    {
        var id = Guid.NewGuid();
        var active = CreateEntry(id, "Active", 2);
        var deleted = CreateEntry(id, "Deleted", 2, isDeleted: true);

        var merged = VaultMerger.Merge(Data(active), Data(deleted));

        Assert.True(Assert.Single(merged.Entries).IsDeleted);
    }

    [Fact]
    public void MergeEntriesOnlyOnOneSideAreKept()
    {
        var localEntry = CreateEntry(Guid.NewGuid(), "Local", 1);
        var remoteEntry = CreateEntry(Guid.NewGuid(), "Remote", 1);

        var merged = VaultMerger.Merge(Data(localEntry), Data(remoteEntry));

        Assert.Equal(
            new[] { localEntry.Id, remoteEntry.Id }.OrderBy(id => id),
            merged.Entries.Select(entry => entry.Id).OrderBy(id => id));
    }

    [Fact]
    public void MergeIsCommutativeForEntriesIgnoringListOrder()
    {
        var conflictId = Guid.NewGuid();
        var deletedTieId = Guid.NewGuid();
        var identicalTieId = Guid.NewGuid();
        var local = Data(
            CreateEntry(conflictId, "Newer local", 4),
            CreateEntry(deletedTieId, "Active", 3),
            CreateEntry(identicalTieId, "Identical", 5),
            CreateEntry(Guid.NewGuid(), "Local only", 2));
        var remote = Data(
            CreateEntry(conflictId, "Older remote", 2),
            CreateEntry(deletedTieId, "Deleted", 3, isDeleted: true),
            CreateEntry(identicalTieId, "Identical", 5),
            CreateEntry(Guid.NewGuid(), "Remote only", 2));

        var left = VaultMerger.Merge(local, remote).Entries;
        var right = VaultMerger.Merge(remote, local).Entries;

        AssertEntriesEqual(left, right);
    }

    [Fact]
    public void MergeCategoriesUseTheSameWinnerRuleAndSettingsStayLocal()
    {
        var id = Guid.NewGuid();
        var local = new VaultData
        {
            Categories = [CreateCategory(id, "Local", 2)],
            Settings = new VaultSettings { AutoLockSeconds = 120, ClipboardClearSeconds = 45 }
        };
        var remote = new VaultData
        {
            Categories = [CreateCategory(id, "Remote deleted", 2, isDeleted: true)],
            Settings = new VaultSettings { AutoLockSeconds = 30, ClipboardClearSeconds = 10 }
        };

        var merged = VaultMerger.Merge(local, remote);

        var category = Assert.Single(merged.Categories);
        Assert.Equal("Remote deleted", category.Name);
        Assert.True(category.IsDeleted);
        Assert.Equal(120, merged.Settings.AutoLockSeconds);
        Assert.Equal(45, merged.Settings.ClipboardClearSeconds);
    }

    private static VaultData Data(params VaultEntry[] entries) => new() { Entries = [.. entries] };

    private static VaultEntry CreateEntry(Guid id, string title, int minutes, bool isDeleted = false)
    {
        return new VaultEntry
        {
            Id = id,
            Title = title,
            Username = $"user-{title}",
            Password = $"password-{title}",
            UpdatedAt = BaseTime.AddMinutes(minutes),
            IsDeleted = isDeleted
        };
    }

    private static Category CreateCategory(Guid id, string name, int minutes, bool isDeleted = false)
    {
        return new Category
        {
            Id = id,
            Name = name,
            UpdatedAt = BaseTime.AddMinutes(minutes),
            IsDeleted = isDeleted
        };
    }

    private static void AssertEntriesEqual(
        IReadOnlyCollection<VaultEntry> expected,
        IReadOnlyCollection<VaultEntry> actual)
    {
        var orderedExpected = expected.OrderBy(entry => entry.Id).ToArray();
        var orderedActual = actual.OrderBy(entry => entry.Id).ToArray();
        Assert.Equal(orderedExpected.Length, orderedActual.Length);
        for (var index = 0; index < orderedExpected.Length; index++)
        {
            Assert.Equal(orderedExpected[index].Id, orderedActual[index].Id);
            Assert.Equal(orderedExpected[index].Title, orderedActual[index].Title);
            Assert.Equal(orderedExpected[index].Username, orderedActual[index].Username);
            Assert.Equal(orderedExpected[index].Password, orderedActual[index].Password);
            Assert.Equal(orderedExpected[index].UpdatedAt, orderedActual[index].UpdatedAt);
            Assert.Equal(orderedExpected[index].IsDeleted, orderedActual[index].IsDeleted);
        }
    }
}
