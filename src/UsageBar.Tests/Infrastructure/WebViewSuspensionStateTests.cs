using UsageBar.Windows.Tooltip;
using Xunit;

namespace UsageBar.Tests.Infrastructure;

public sealed class WebViewSuspensionStateTests
{
    [Fact]
    public void Completed_suspend_after_resume_request_is_immediately_resumed()
    {
        var state = new WebViewSuspensionState();

        Assert.True(state.RequestSuspend());
        Assert.False(state.RequestResume());

        Assert.True(state.CompleteSuspend(suspended: true));
        Assert.False(state.IsSuspended);
    }

    [Fact]
    public void Completed_suspend_without_resume_request_stays_suspended()
    {
        var state = new WebViewSuspensionState();

        Assert.True(state.RequestSuspend());

        Assert.False(state.CompleteSuspend(suspended: true));
        Assert.True(state.IsSuspended);
        Assert.True(state.RequestResume());
        Assert.False(state.IsSuspended);
    }

    [Fact]
    public void Hide_after_in_flight_resume_keeps_completed_suspend()
    {
        var state = new WebViewSuspensionState();

        Assert.True(state.RequestSuspend());
        Assert.False(state.RequestResume());
        Assert.False(state.RequestSuspend());

        Assert.False(state.CompleteSuspend(suspended: true));
        Assert.True(state.IsSuspended);
    }
}
