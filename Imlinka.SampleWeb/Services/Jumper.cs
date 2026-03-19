namespace Imlinka.SampleWeb.Services;

public sealed class Jumper : IJumper
{
    public async Task Jump()
    {
        await Task.Delay(300);
    }
}