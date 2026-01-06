using Microsoft.VisualStudio.TestTools.UnitTesting;
using F1_simulation;

namespace Tests
{
    [TestClass]
    public sealed class Test1
    {
        [TestMethod]
        public void TestMethod1_ShouldPass()
        {
            // Example: simple math test
            int a = 2;
            int b = 3;
            int sum = a + b;

            Assert.AreEqual(5, sum, "2 + 3 should equal 5");
        }
    }
}
