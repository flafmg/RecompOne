namespace RecompOne.Runtime.Host.Window;

internal interface IPanel
{
    string Name { get; }
    bool IsOpen { get; set; }
    void Draw();
}
