namespace BX.SJ.Tests;

public static class TestTimeouts
{
    public const int Short = 1000;
    public const int Mid = 5000;
    public const int Long = 30000;
    public const int Infinite = (int)Microsoft.VisualStudio.TestTools.UnitTesting.TestTimeout.Infinite;
}
