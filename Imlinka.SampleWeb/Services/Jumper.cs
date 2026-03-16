namespace Imlinka.SampleWeb.Services;

public sealed class Jumper : IJumper
{
    public async Task Jump()
    {
        await Task.Delay(300);
        await JumpHigher();
    }

    private async Task JumpHigher()
    {
        await Task.Delay(150);
    }
}