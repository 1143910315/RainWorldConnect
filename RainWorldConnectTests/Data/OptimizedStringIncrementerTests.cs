namespace RainWorldConnect.Data.Tests {
    [TestClass()]
    public class OptimizedStringIncrementerTests {
        [TestMethod()]
        public void IncrementStringTest() {
            HashSet<string> set = [];
            string s = "";
            for (int i = 0; i < 1000000; i++) {
                //Console.WriteLine(s);
                Assert.IsTrue(set.Add(s), "String not unique: " + s);
                s = AdvancedStringIncrementer.IncrementString(s);
            }
        }
    }
}