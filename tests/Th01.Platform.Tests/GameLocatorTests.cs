using Th01.Assets.Tests;
using Th01.Platform.MonoGame.Launcher;

namespace Th01.Platform.Tests;

public class GameLocatorTests : IDisposable
{
    sealed class FakePrompt(string? answer) : IUserPrompt
    {
        public int Asked, Errors;
        public string? PickGameFile() { Asked++; return answer; }
        public void ShowError(string message) => Errors++;
    }

    readonly string _dir = Directory.CreateTempSubdirectory("th01-loc-").FullName;
    string UserDir => Path.Combine(_dir, "user");

    string MakeHdi(bool complete = true)
    {
        string hdi = Path.Combine(_dir, complete ? "game.hdi" : "other.hdi");
        File.WriteAllBytes(hdi, SyntheticHdi.Build(SyntheticHdi.FakeGameFiles(complete)));
        return hdi;
    }

    GameLocator Locator(IUserPrompt p) => new(UserDir, p, developerPath: () => null);

    [Fact]
    public void FirstRunAsks_ThenRemembersTheChoice()
    {
        var prompt = new FakePrompt(MakeHdi());
        string? first = Locator(prompt).Resolve(null, null, false);
        Assert.NotNull(first);
        Assert.Equal(1, prompt.Asked);

        var second = new FakePrompt(null); // would cancel if it were asked
        Assert.Equal(first, Locator(second).Resolve(null, null, false));
        Assert.Equal(0, second.Asked);
    }

    [Fact]
    public void ForceSelect_AsksAgain()
    {
        var prompt = new FakePrompt(MakeHdi());
        Locator(prompt).Resolve(null, null, false);
        Locator(prompt).Resolve(null, null, forceSelect: true);
        Assert.Equal(2, prompt.Asked);
    }

    [Fact]
    public void Cancelled_ReturnsNull_AndRemembersNothing()
    {
        var prompt = new FakePrompt(null);
        Assert.Null(Locator(prompt).Resolve(null, null, false));
        Assert.Null(Locator(prompt).LoadState());
        Assert.Equal(0, prompt.Errors);
    }

    [Fact]
    public void InvalidImage_ShowsError_AndRemembersNothing()
    {
        var prompt = new FakePrompt(MakeHdi(complete: false));
        Assert.Null(Locator(prompt).Resolve(null, null, false));
        Assert.Equal(1, prompt.Errors);
        Assert.Null(Locator(prompt).LoadState());
    }

    [Fact]
    public void DeletedCache_AsksAgain()
    {
        var prompt = new FakePrompt(MakeHdi());
        string data = Locator(prompt).Resolve(null, null, false)!;
        Directory.Delete(data, true);
        Locator(prompt).Resolve(null, null, false);
        Assert.Equal(2, prompt.Asked);
    }

    [Fact]
    public void DataArgument_IsUsedAsIs_AndNotRemembered()
    {
        var prompt = new FakePrompt(null);
        Assert.Equal(@"C:\somewhere", Locator(prompt).Resolve(@"C:\somewhere", null, false));
        Assert.Null(Locator(prompt).LoadState());
        Assert.Equal(0, prompt.Asked);
    }

    [Fact]
    public void GameArgument_SkipsTheDialog_AndIsRemembered()
    {
        var prompt = new FakePrompt(null);
        string? path = Locator(prompt).Resolve(null, MakeHdi(), false);
        Assert.NotNull(path);
        Assert.Equal(0, prompt.Asked);
        Assert.NotNull(Locator(prompt).LoadState());
    }

    public void Dispose() => Directory.Delete(_dir, true);
}
