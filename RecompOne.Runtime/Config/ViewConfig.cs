namespace RecompOne.Runtime.Config;

public class PanelState
{
    public bool Open { get; set; }
}

public class ViewConfig
{
    public bool HideTopBar { get; set; } = false;
    public Dictionary<string, PanelState> Panels   { get; set; } = [];
}
