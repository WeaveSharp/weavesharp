using Microsoft.VisualStudio.Extensibility.UI;

namespace WeaveSharp.VisualStudio.Spike;

internal sealed class SpikeContent : RemoteUserControl
{
    public SpikeContent()
        : base(dataContext: null)
    {
    }
}
