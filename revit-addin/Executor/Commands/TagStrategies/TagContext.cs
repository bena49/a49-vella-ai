// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/TagContext.cs
// ============================================================================
// Shared data classes used by the Automate Tagging system:
//   - TagRequest: input payload from the wizard
//   - TagResult:  per-view result returned from a strategy
// ============================================================================

using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace A49AIRevitAssistant.Executor.Commands.TagStrategies
{
    /// <summary>
    /// Single wizard submission — what to tag, where to tag it, how.
    /// </summary>
    public class TagRequest
    {
        public string TagTypeCategory { get; set; }  // "door", "window", "wall", "room", "ceiling"
        public string TagFamily { get; set; }
        public string TagTypeName { get; set; }
        public List<ElementId> ViewIds { get; set; }
        public bool SkipTagged { get; set; } = true;
    }

    /// <summary>
    /// Per-view tagging result aggregated by the orchestrator.
    /// </summary>
    public class TagResult
    {
        public int Tagged { get; set; } = 0;
        public int Skipped { get; set; } = 0;
        public List<string> Errors { get; set; } = new List<string>();
    }
}
