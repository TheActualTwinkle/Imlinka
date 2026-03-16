namespace Imlinka.Benchmarks.BenchServices;

internal static class FibonacciCalculator
{
    public static int Compute(int n)
    {
        switch (n)
        {
            case <= 0:
                return 0;
            case 1:
                return 1;
        }

        var a = 0;
        var b = 1;
        
        for (var i = 2; i <= n; i++)
        {
            var c = a + b;
            a = b;
            b = c;
        }

        return b;
    }
}