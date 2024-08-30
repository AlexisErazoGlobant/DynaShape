using System.Diagnostics;
using DynaShape;
using DynaShape.Goals;

namespace DynaShapeTests;

public class BasicTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void LengthGoalTest()
    {
        Solver solver = new Solver();
        LengthGoal lengthGoal = new LengthGoal(new Triple(0f, 0f, 0f), new Triple(2f, 0f, 0f), 0f);
        solver.RegisterGoal(lengthGoal);
        solver.Iterate(10);
        List<Triple> nodePositions = solver.GetNodePositions();
        float xCoordinate = nodePositions[0].X;
        Assert.AreEqual(xCoordinate, 0f);
        Assert.Pass();
    }
}