using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.DimStrategies
{
    /// <summary>
    /// Contract for all dimensioning strategies.
    /// Each strategy handles one element type (walls, grids, etc.)
    /// Mirrors the ITagStrategy pattern from AutoTag.
    /// </summary>
    public interface IDimStrategy
    {
        /// <summary>
        /// Human-readable name for logging and error messages.
        /// e.g. "WallDimStrategy"
        /// </summary>
        string StrategyName { get; }

        /// <summary>
        /// Returns true if this strategy can process the given element
        /// in the given view. Used by AutoDimCommand to route elements.
        /// </summary>
        bool CanDimension(Element element, View view);

        /// <summary>
        /// Execute dimensioning for a single element.
        /// Called once per element by the AutoDimCommand orchestrator.
        /// All Revit writes must happen inside a Transaction managed by the caller.
        /// </summary>
        DimResult Dimension(Element element, DimContext context);
    }
}
