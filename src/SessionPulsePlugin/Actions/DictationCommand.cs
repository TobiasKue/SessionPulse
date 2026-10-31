namespace Loupedeck.SessionPulsePlugin;

/// <summary>
/// Optional: starts dictation in a configured chat by pressing Claude Desktop's own dictation button; a
/// second press stops it. Off (gray, "not set up") until dictation.json exists (docs/configuration.md).
/// Windows only.
/// </summary>
public sealed class DictationCommand : PluginDynamicCommand
{
    public DictationCommand()
        : base(displayName: "Dictation", description: "Start or stop dictation in a configured Claude chat (optional, Windows)", groupName: "SessionPulse")
    {
    }

    private SessionPulsePlugin Owner => (SessionPulsePlugin)this.Plugin;

    internal void Redraw() => this.ActionImageChanged();

    protected override bool OnLoad()
    {
        this.Owner.RegisterDictation(this);
        return true;
    }

    protected override void RunCommand(string actionParameter) => this.Owner.ToggleDictation();

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => "Dictation";

    protected override BitmapImage? GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        var owner = this.Owner;
        owner.Diagnostics.CountImageRequest();
        var (recording, configured) = owner.DictationState;
        var label = recording ? owner.Texts.DictationActive : configured ? owner.Texts.Dictation : owner.Texts.NotConfigured;
        try
        {
            return owner.Renderer.RenderTile(TileIcon.Microphone, null, label, recording, imageSize, owner.Frame);
        }
        catch (Exception ex)
        {
            owner.Diagnostics.LogDrawError("dictation", ex);
            return owner.Renderer.RenderError(imageSize);
        }
    }
}
