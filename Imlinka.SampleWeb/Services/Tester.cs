namespace Imlinka.SampleWeb.Services;

public sealed class Tester(IJumper jumper) : ITester
{
    public async Task Test()
    {
        await Task.Delay(500);
        await jumper.Jump();
    }
}

