using Microsoft.VisualStudio.Extensibility;

namespace WeaveSharp.VisualStudio.Spike;

[VisualStudioContribution]
internal sealed class SpikeExtension : Extension
{
    public override ExtensionConfiguration ExtensionConfiguration => new()
    {
        Metadata = new(
            "WeaveSharp.VisualStudio.Spike",
            this.ExtensionAssemblyVersion,
            "WeaveSharp",
            "%Spike.DisplayName%",
            "%Spike.Description%"),
    };
}
