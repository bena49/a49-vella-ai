// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/ITagStrategy.cs
// ============================================================================
// Contract for per-category tag placement strategies.
// Each strategy (DoorTagStrategy, WindowTagStrategy, etc.) implements this
// interface to handle its own element collection and tag placement logic.
// ============================================================================

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    public interface ITagStrategy
    {
        /// <summary>
        /// Category key this strategy handles ("door", "window", "wall", "room", "ceiling").
        /// </summary>
        string CategoryKey { get; }

        /// <summary>
        /// Built-in Revit category for the elements this strategy tags.
        /// </summary>
        BuiltInCategory TargetCategory { get; }

        /// <summary>
        /// Built-in Revit category for the tags this strategy places.
        /// </summary>
        BuiltInCategory TagCategory { get; }

        /// <summary>
        /// Returns true if the given view type is supported by this strategy.
        /// (e.g. Door is valid in FloorPlan/Elevation/Section but not CeilingPlan)
        /// </summary>
        bool SupportsViewType(ViewType viewType);

        /// <summary>
        /// Tags all eligible elements in the given view.
        /// Strategy is responsible for:
        ///   - Collecting elements visible in the view
        ///   - Skipping already-tagged elements if requested
        ///   - Calculating placement position + orientation + leader for each element
        ///   - Calling IndependentTag.Create() inside the caller's transaction
        /// </summary>
        TagResult TagElementsInView(
            Document doc,
            View view,
            FamilySymbol tagSymbol,
            bool skipTagged);
    }
}
