using DynaShape;
using DynaShape.Goals;

namespace DynaShapeTests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void HangingChainTest()
    {
        Solver solver = new Solver();

        LengthGoal lengthGoal = new LengthGoal(new Triple(0f, 0f, 0f), new Triple(2f, 2f, 2f), 0f);
        solver.RegisterGoal(lengthGoal);
        solver.Iterate();

        Assert.Pass();
    }
}