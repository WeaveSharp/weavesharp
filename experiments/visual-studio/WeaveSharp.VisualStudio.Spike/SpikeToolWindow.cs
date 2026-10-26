using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.ToolWindows;
using Microsoft.VisualStudio.RpcContracts.RemoteUI;

namespace WeaveSharp.VisualStudio.Spike;

[VisualStudioContribution]
internal sealed class SpikeToolWindow : ToolWindow
{
    private readonly SpikeContent content = new();

    public SpikeToolWindow()
    {
        this.Title = "WeaveSharp host spike";
    }

    public override ToolWindowConfiguration ToolWindowConfiguration => new()
    {
        Placement = ToolWindowPlacement.Floating,
        AllowAutoCreation = false,
    };

    public override Task<IRemoteUserControl> GetContentAsync(CancellationToken cancellationToken)
        => Task.FromResult<IRemoteUserControl>(this.content);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.content.Dispose();
        }

        base.Dispose(disposing);
    }
}
