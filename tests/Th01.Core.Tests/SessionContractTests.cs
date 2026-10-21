using Th01.Core.Graphics;
using Th01.Core.Input;
using Th01.Core.Sim;

namespace Th01.Core.Tests;

public class SessionContractTests
{
    sealed class Minimal : IGameSession
    {
        public void Step(GameButtons input) { }
        public void Render(Framebuffer target) { }
        public ulong StateHash() => 0;
    }

    [Fact]
    public void Outcome_DefaultsToRunning_ForExistingImplementations()
    {
        IGameSession s = new Minimal();
        Assert.Equal(SessionOutcome.Running, s.Outcome);
    }
}
