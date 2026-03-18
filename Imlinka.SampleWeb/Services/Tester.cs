namespace Imlinka.SampleWeb.Services;

public sealed class Tester(IJumper jumper) : ITester
{
    public async Task Test()
    {
        await Task.Delay(500);
        
        var t1 = jumper.Jump();
        var t2 = jumper.Jump();
        var t3 = jumper.Jump();
        
        await Task.WhenAll(t1, t2, t3);
    }
}

