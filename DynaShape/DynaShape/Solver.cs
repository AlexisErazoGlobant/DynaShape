﻿using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Autodesk.DesignScript.Runtime;
using Dynamo.Graph.Workspaces;
using Dynamo.Wpf.ViewModels.Watch3D;
using DynaShape.Goals;
using DynaShape.GeometryBinders;

using Point = Autodesk.DesignScript.Geometry.Point;
using Vector = Autodesk.DesignScript.Geometry.Vector;

namespace DynaShape;


[IsVisibleInDynamoLibrary(false)]
public class Solver : IDisposable
{
    /// <summary>
    ///
    /// </summary>
    public bool EnableMouseInteraction = true;

    /// <summary>
    ///
    /// </summary>
    public bool EnableMomentum = true;

    /// <summary>
    ///
    /// </summary>
    public bool EnableFastDisplay = true;

    /// <summary>
    ///
    /// </summary>
    public float DampingFactor = 0.98f;

    /// <summary>
    ///
    /// </summary>
    public int IterationCount = 0;

    /// <summary>
    ///
    /// </summary>
    public int CurrentIteration { get; private set; } = 0;

    /// <summary>
    ///
    /// </summary>
    public List<Goal> Goals = new List<Goal>();

    /// <summary>
    ///
    /// </summary>
    public List<GeometryBinder> GeometryBinders = new List<GeometryBinder>();

    /// <summary>
    ///
    /// </summary>
    public List<Node> Nodes = new List<Node>();

    internal DynaShapeDisplay Display;
    internal int HandleNodeIndex = -1;
    internal int NearestNodeIndex = -1;

    private Task backgroundExecutionTask;
    private CancellationTokenSource ctSource;


    public Solver()
    {
        SetUpDisplayAndUserInteraction();
    }


    internal void SetUpDisplayAndUserInteraction()
    {
        if (Display == null &&DynaShapeViewExtension.ViewModel != null) // This check is important in case ViewModel is null (e.g. in Refinery mode)
        {
            Display = new DynaShapeDisplay(this);
            DynaShapeViewExtension.Parameters.CurrentWorkspaceCleared += CurrentWorkspaceClearedHandler;
            DynaShapeViewExtension.ViewModel.ViewMouseDown += ViewportMouseDownHandler;
            DynaShapeViewExtension.ViewModel.ViewMouseUp += ViewportMouseUpHandler;
            DynaShapeViewExtension.ViewModel.ViewMouseMove += ViewportMouseMoveHandler;
            DynaShapeViewExtension.ViewModel.ViewCameraChanged += ViewportCameraChangedHandler;
            DynaShapeViewExtension.ViewModel.CanNavigateBackgroundPropertyChanged += ViewportCanNavigateBackgroundPropertyChangedHandler;
        }
    }


    public void RegisterGoals(IEnumerable<Goal> goals, double nodeMergeThreshold = 0.0001, bool keepExistingNodeIndices = false)
    {
        foreach (Goal goal in goals)
            RegisterGoal(goal, nodeMergeThreshold, keepExistingNodeIndices);
    }


    public void RemoveGoal(Goal goal)
    {
        Goals.Remove(goal);
    }


    public void RegisterGeometryBinders(IEnumerable<GeometryBinder> geometryBinders, double nodeMergeThreshold = 0.0001, bool keepExistingNodeIndices = false)
    {
        foreach (GeometryBinder geometryBinder in geometryBinders)
            RegisterGeometryBinder(geometryBinder, nodeMergeThreshold, keepExistingNodeIndices);
    }


    public void RegisterGoal(Goal goal, double nodeMergeThreshold = 0.0001, bool keepExistingNodeIndices = false)
    {
        if (goal == null) return;

        Goals.Add(goal);

        if (keepExistingNodeIndices) return;

        goal.NodeIndices = new int[goal.NodeCount];

        for (int i = 0; i < goal.NodeCount; i++)
        {
            bool nodeAlreadyExist = false;

            for (int j = 0; j < Nodes.Count; j++)
                if ((goal.StartingPositions[i] - Nodes[j].Position).LengthSquared <
                    nodeMergeThreshold * nodeMergeThreshold)
                {
                    goal.NodeIndices[i] = j;
                    nodeAlreadyExist = true;
                    break;
                }

            if (!nodeAlreadyExist)
            {
                Nodes.Add(new Node(goal.StartingPositions[i]));
                goal.NodeIndices[i] = Nodes.Count - 1;
            }
        }
    }


    public void RegisterGeometryBinder(GeometryBinder geometryBinder, double nodeMergeThreshold = 0.0001, bool keepExistingNodeIndices = false)
    {
        if (geometryBinder == null) return;

        GeometryBinders.Add(geometryBinder);

        if (keepExistingNodeIndices) return;

        geometryBinder.NodeIndices = new int[geometryBinder.NodeCount];

        for (int i = 0; i < geometryBinder.NodeCount; i++)
        {
            bool nodeAlreadyExist = false;

            for (int j = 0; j < Nodes.Count; j++)
                if ((geometryBinder.StartingPositions[i] - Nodes[j].Position).LengthSquared <
                    nodeMergeThreshold * nodeMergeThreshold)
                {
                    geometryBinder.NodeIndices[i] = j;
                    nodeAlreadyExist = true;
                    break;
                }

            if (!nodeAlreadyExist)
            {
                Nodes.Add(new Node(geometryBinder.StartingPositions[i]));
                geometryBinder.NodeIndices[i] = Nodes.Count - 1;
            }
        }
    }


    public List<Triple> GetNodePositions()
    {
        List<Triple> nodePositions = new List<Triple>(Nodes.Count);
        for (int i = 0; i < Nodes.Count; i++)
            nodePositions.Add(Nodes[i].Position);
        return nodePositions;
    }


    public List<Point> GetNodePositionsAsPoints()
    {
        List<Point> nodePositions = new List<Point>(Nodes.Count);
        for (int i = 0; i < Nodes.Count; i++)
            nodePositions.Add(Nodes[i].Position.ToPoint());
        return nodePositions;
    }


    public List<List<Triple>> GetStructuredNodePositions()
    {
        List<List<Triple>> nodePositions = new List<List<Triple>>(Goals.Count);
        for (int i = 0; i < Goals.Count; i++)
        {
            List<Triple> goalNodePositions = new List<Triple>(Goals[i].NodeCount);
            for (int j = 0; j < Goals[i].NodeCount; j++)
                goalNodePositions.Add(Nodes[Goals[i].NodeIndices[j]].Position);
            nodePositions.Add(goalNodePositions);
        }
        return nodePositions;
    }


    public List<List<Point>> GetStructuredNodePositionsAsPoints()
    {
        List<List<Point>> nodePositions = new List<List<Point>>(Goals.Count);
        for (int i = 0; i < Goals.Count; i++)
        {
            List<Point> goalNodePositions = new List<Point>(Goals[i].NodeCount);
            for (int j = 0; j < Goals[i].NodeCount; j++)
                goalNodePositions.Add(Nodes[Goals[i].NodeIndices[j]].Position.ToPoint());
            nodePositions.Add(goalNodePositions);
        }
        return nodePositions;
    }


    public List<Triple> GetNodeVelocities()
    {
        List<Triple> nodeVelocities = new List<Triple>(Nodes.Count);
        for (int i = 0; i < Nodes.Count; i++)
            nodeVelocities.Add(Nodes[i].Velocity);
        return nodeVelocities;
    }


    public List<Vector> GetNodeVelocitiesAsVectors()
    {
        List<Vector> nodeVelocities = new List<Vector>(Nodes.Count);
        foreach (Node node in Nodes)
            nodeVelocities.Add(node.Velocity.ToVector());
        return nodeVelocities;
    }


    public List<List<object>> GetGeometries()
    {
        List<List<object>> geometries = new List<List<object>>(GeometryBinders.Count);
        foreach (GeometryBinder geometryBinder in GeometryBinders)
            if (geometryBinder.Show)
                geometries.Add(geometryBinder.CreateGeometryObjects(Nodes));
        return geometries;
    }


    public List<List<object>> GetGeometries(IEnumerable<GeometryBinder> geometryBinders)
    {
        List<List<object>> geometries = new List<List<object>>(GeometryBinders.Count);
        foreach (GeometryBinder geometryBinder in geometryBinders)
            if (geometryBinder.Show)
                geometries.Add(geometryBinder.CreateGeometryObjects(Nodes));
        return geometries;
    }


    public List<object> GetGeometries(GeometryBinder geometryBinder)
    {
        return geometryBinder.CreateGeometryObjects(Nodes);
    }


    public List<List<object>> GetGoalOutputs()
    {
        List<List<object>> goalOutputs = new List<List<object>>(Goals.Count);
        foreach (Goal goal in Goals) goalOutputs.Add(goal.GetOutputs(Nodes));
        return goalOutputs;
    }

    public void Clear()
    {
        Nodes.Clear();
        Goals.Clear();
        GeometryBinders.Clear();
        CurrentIteration = 0;
    }


    public void Reset()
    {
        CurrentIteration = 0;
        foreach (Node node in Nodes) node.Reset();
    }


    public void Iterate()
    {
        CurrentIteration++;

        //=================================================================================
        // Apply momentum
        //=================================================================================

        if (EnableMomentum)
            foreach (Node node in Nodes)
                node.Position += node.Velocity;

        //=================================================================================
        // Process each goal independently, in parallel
        //=================================================================================

        Parallel.ForEach(Goals, goal => goal.Compute(Nodes));

        //=================================================================================
        // Compute the total move vector that acts on each node
        //=================================================================================

        Triple[] nodeMoveSums = new Triple[Nodes.Count];
        float[] nodeWeightSums = new float[Nodes.Count];

        for (int j = 0; j < Goals.Count; j++)
        {
            Goal goal = Goals[j];
            for (int i = 0; i < goal.NodeCount; i++)
            {
                nodeMoveSums[goal.NodeIndices[i]] += goal.Moves[i] * goal.Weights[i];
                nodeWeightSums[goal.NodeIndices[i]] += goal.Weights[i];
            }
        }

        //=================================================================================
        // Move the manipulated node toward the mouse ray
        //=================================================================================


        if (HandleNodeIndex != -1)
        {
            float manipulationWeight = 30f;
            nodeWeightSums[HandleNodeIndex] += manipulationWeight;

            Triple v = Nodes[HandleNodeIndex].Position - DynaShapeViewExtension.MouseRayOrigin;
            Triple mouseRayPull = v.Dot(DynaShapeViewExtension.MouseRayDirection) * DynaShapeViewExtension.MouseRayDirection - v;
            nodeMoveSums[HandleNodeIndex] += manipulationWeight * mouseRayPull;
        }


        //=============================================================================================
        // Move the nodes to their new positions
        //=============================================================================================

        if (EnableMomentum)
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (nodeWeightSums[i] == 0f) continue;
                Triple move = nodeMoveSums[i] / nodeWeightSums[i];
                Nodes[i].Move = move;
                Nodes[i].Position += move;
                Nodes[i].Velocity += move;
                //if (Nodes[i].Velocity.Dot(move) <= 0.0)
                Nodes[i].Velocity *= DampingFactor;
            }
        else
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (nodeWeightSums[i] == 0f) continue;
                Triple move = nodeMoveSums[i] / nodeWeightSums[i];
                Nodes[i].Move = move;
                Nodes[i].Position += move;
                Nodes[i].Velocity = Triple.Zero;
            }
    }


    public void Iterate(int iterationCount)
    {
        for (int i = 0; i < iterationCount; i++) Iterate();
    }


    public void Iterate(float miliseconds)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalMilliseconds < miliseconds)
            Iterate();
    }


    public void Execute(int maxIterationCount, float keThreshold)
    {
        while (CurrentIteration < maxIterationCount)
        {
            Iterate();
            if (GetKineticEnergy() < keThreshold) break;
        }
    }


    public float GetKineticEnergy()
    {
        float ke = 0f;

        for (int i = 0; i < Nodes.Count; i++)
            ke += Nodes[i].Velocity.LengthSquared;

        return ke;
    }


    public float GetLargestMove()
    {
        float largestMove = 0f;

        foreach (Node node in Nodes)
        {
            float move = node.Move.Length;
            if (move > largestMove) largestMove = move;
        }

        return largestMove;
    }


    public void ClearRender() { Display?.ClearRender(); }

    public void Render() { Display.Render(); }


    public void StartBackgroundExecution()
    {
        if (backgroundExecutionTask != null && backgroundExecutionTask.Status == TaskStatus.Running) return;
        ctSource = new CancellationTokenSource();
        backgroundExecutionTask = Task.Factory.StartNew(BackgroundExecutionAction, ctSource.Token);
    }


    public void StopBackgroundExecution()
    {
        if (backgroundExecutionTask == null) return;
        ctSource?.Cancel();
        backgroundExecutionTask?.Wait(300);
        Display.DispatcherOperation?.Task.Wait(300);
    }


    private void BackgroundExecutionAction()
    {
        while (!ctSource.Token.IsCancellationRequested)
        {
            if (IterationCount > 0)
                Iterate(IterationCount);
            else
                Iterate(25f);

            if (EnableFastDisplay)
                Display.Render(true);
        }
    }


    internal int FindNearestNodeIndex(float range = 0.03f)
    {
        CameraData cameraData = DynaShapeViewExtension.CameraData;

        Triple camZ = new Triple(cameraData.LookDirection.X,
            -cameraData.LookDirection.Z,
            cameraData.LookDirection.Y).Normalise();

        Triple camY = new Triple(cameraData.UpDirection.X,
            -cameraData.UpDirection.Z,
            cameraData.UpDirection.Y).Normalise();

        Triple camX = camY.Cross(camZ).Normalise();

        Triple mousePosition2D = new Triple(DynaShapeViewExtension.MouseRayDirection.Dot(camX),
            DynaShapeViewExtension.MouseRayDirection.Dot(camY),
            DynaShapeViewExtension.MouseRayDirection.Dot(camZ));

        mousePosition2D /= mousePosition2D.Z;

        int nearestNodeIndex = -1;

        float minDistSquared = range * range;

        for (int i = 0; i < Nodes.Count; i++)
        {
            Triple v = Nodes[i].Position - DynaShapeViewExtension.MouseRayOrigin;
            v = new Triple(v.Dot(camX), v.Dot(camY), v.Dot(camZ));
            Triple nodePosition2D = v / v.Z;

            float distSquared = (mousePosition2D - nodePosition2D).LengthSquared;

            if (distSquared < minDistSquared)
            {
                minDistSquared = distSquared;
                nearestNodeIndex = i;
            }
        }

        return nearestNodeIndex;
    }


    private void ViewportCameraChangedHandler(object sender, RoutedEventArgs args)
    {
        NearestNodeIndex = -1;
    }


    private void ViewportMouseDownHandler(object sender, MouseButtonEventArgs args)
    {
        if (args.LeftButton == MouseButtonState.Pressed && EnableMouseInteraction)
            HandleNodeIndex = FindNearestNodeIndex();
    }


    private void ViewportMouseUpHandler(object sender, MouseButtonEventArgs args)
    {
        HandleNodeIndex = -1;
        NearestNodeIndex = -1;
    }


    private void ViewportMouseMoveHandler(object sender, MouseEventArgs args)
    {
        if (!EnableMouseInteraction) return;
        if (args.LeftButton == MouseButtonState.Released) HandleNodeIndex = -1;
        NearestNodeIndex = FindNearestNodeIndex();
    }


    private void ViewportCanNavigateBackgroundPropertyChangedHandler(bool canNavigate)
    {
        HandleNodeIndex = -1;
        NearestNodeIndex = -1;
    }


    private void CurrentWorkspaceClearedHandler(IWorkspaceModel obj)
    {
        Dispose();
    }


    public void Dispose()
    {
        StopBackgroundExecution();
        Clear();

        if (DynaShapeViewExtension.ViewModel != null) // This check is important in case ViewModel is null (e.g. running in CLI mode)
        {
            DynaShapeViewExtension.ViewModel.ViewMouseDown -= ViewportMouseDownHandler;
            DynaShapeViewExtension.ViewModel.ViewMouseUp -= ViewportMouseUpHandler;
            DynaShapeViewExtension.ViewModel.ViewMouseMove -= ViewportMouseMoveHandler;
            DynaShapeViewExtension.ViewModel.ViewCameraChanged -= ViewportCameraChangedHandler;
            DynaShapeViewExtension.ViewModel.CanNavigateBackgroundPropertyChanged -= ViewportCanNavigateBackgroundPropertyChangedHandler;
            Display.Dispose();
            Display = null;
        }

        if (DynaShapeViewExtension.Parameters != null)
        {
            DynaShapeViewExtension.Parameters.CurrentWorkspaceCleared -= CurrentWorkspaceClearedHandler;
        }
    }
}