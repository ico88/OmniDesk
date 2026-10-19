using System.ComponentModel;
using OmniDesk.Windows.Ipc;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class PipeConnectionRetryTests
{
    [Theory]
    [InlineData(2)] [InlineData(231)]
    public async Task Transient_contention_waits_then_opens(int error)
    {
        var attempts=0;
        var result=await PipeConnectionRetry.Open(()=>++attempts==1?throw new Win32Exception(error):"verified",CancellationToken.None);
        Assert.Equal("verified",result);Assert.Equal(2,attempts);
    }
    [Fact]
    public async Task Access_denial_is_never_retried()
    {
        var attempts=0;
        await Assert.ThrowsAsync<Win32Exception>(()=>PipeConnectionRetry.Open<string>(()=>{attempts++;throw new Win32Exception(5);},CancellationToken.None));
        Assert.Equal(1,attempts);
    }
    [Fact]
    public async Task Busy_pipe_obeys_cancellation_deadline()
    {
        using var stop=new CancellationTokenSource();
        var attempts=0;
        var pending=PipeConnectionRetry.Open<string>(()=>{attempts++;throw new Win32Exception(231);},stop.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);
        Assert.Equal(1,attempts);
    }
}
