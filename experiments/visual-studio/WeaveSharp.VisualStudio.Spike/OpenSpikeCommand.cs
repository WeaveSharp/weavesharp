using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Extensibility;
using Microsoft.VisualStudio.Extensibility.Commands;

namespace WeaveSharp.VisualStudio.Spike;

[VisualStudioContribution]
internal sealed class OpenSpikeCommand : Command
{
    public override CommandConfiguration CommandConfiguration => new("%Spike.OpenWindow%")
    {
        Placements = new[] { CommandPlacement.KnownPlacements.ExtensionsMenu },
    };

    public override async Task ExecuteCommandAsync(IClientContext context, CancellationToken cancellationToken)
    {
        await this.Extensibility.Shell()
            .ShowToolWindowAsync<SpikeToolWindow>(activate: true, cancellationToken)
            .ConfigureAwait(false);
    }
}
