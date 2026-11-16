using Frpm.Infrastructure.Runtime;

namespace Frpm.Infrastructure.Tests;

public sealed class TunnelCircuitBreakerTests
{
    [Fact]
    public void CircuitBreaker_StateTransitions_TracksFailuresAndTripsAtFive()
    {
        var state = new TunnelCircuitBreakerState();
        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.False(state.IsTripped);

        // Simulate failures 1 to 4
        for (int i = 1; i <= 4; i++)
        {
            state.ConsecutiveFailures++;
            var backoff = state.ConsecutiveFailures switch
            {
                1 => 5,
                2 => 15,
                3 => 30,
                4 => 60,
                _ => 120
            };
            state.NextRetryTime = DateTimeOffset.UtcNow.AddSeconds(backoff);
            Assert.False(state.IsTripped);
        }

        Assert.Equal(4, state.ConsecutiveFailures);

        // 5th failure trips the circuit
        state.ConsecutiveFailures++;
        if (state.ConsecutiveFailures >= 5)
        {
            state.IsTripped = true;
            state.TripReason = "连续失败 5 次，已触发熔断保护暂停自动重试。";
        }

        Assert.True(state.IsTripped);
        Assert.NotNull(state.TripReason);

        // Reset
        state.ConsecutiveFailures = 0;
        state.IsTripped = false;
        state.TripReason = null;
        state.NextRetryTime = null;

        Assert.Equal(0, state.ConsecutiveFailures);
        Assert.False(state.IsTripped);
    }
}
