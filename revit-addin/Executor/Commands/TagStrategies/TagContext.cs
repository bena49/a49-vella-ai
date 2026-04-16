// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/TagContext.cs
// ============================================================================
// Shared data classes and helpers used by the Automate Tagging system:
//   - TagRequest: input payload from the wizard
//   - TagResult:  per-view result returned from a strategy
//   - TagHelpers: static helpers for section-cut / facing-elevation checks
// ============================================================================

using System;
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

    /// <summary>
    /// Shared helper methods used by multiple tag strategies.
    /// </summary>
    public static class TagHelpers
    {
        /// <summary>
        /// For SECTION views: checks whether the element's location point 
        /// falls within the section's cut plane (near/far clip range).
        /// Only elements truly "cut" by the section are considered visible.
        /// Elements in the background (behind the cut plane) are excluded.
        /// </summary>
        public static bool IsElementCutBySection(View sectionView, XYZ elementPoint)
        {
            try
            {
                if (sectionView.ViewType != ViewType.Section) return true; // Not a section — don't filter

                BoundingBoxXYZ cropBox = sectionView.CropBox;
                if (cropBox == null) return true; // No crop box — allow

                // Transform element's world point into the view's local coordinate system.
                // In section view local coords:
                //   X = left/right in the section view
                //   Y = up/down in the section view  
                //   Z = depth (near clip → far clip)
                Transform inv = cropBox.Transform.Inverse;
                XYZ localPoint = inv.OfPoint(elementPoint);

                // Check if the element is within the near/far clip range (Z axis in local coords).
                // Use a generous tolerance (500mm in feet) since doors/windows have width.
                double tolerance = 500.0 / 304.8;
                double minZ = cropBox.Min.Z - tolerance;
                double maxZ = cropBox.Max.Z + tolerance;

                return localPoint.Z >= minZ && localPoint.Z <= maxZ;
            }
            catch
            {
                return true; // On error, default to including
            }
        }

        /// <summary>
        /// For ELEVATION views (including interior elevations): checks whether 
        /// the element's host wall is on the FACING wall of the elevation.
        /// Elements on walls behind or perpendicular to the view are excluded.
        /// 
        /// Logic: The elevation's ViewDirection vector points INTO the view 
        /// (from viewer toward the wall being looked at). A wall "faces" the
        /// elevation if its normal is roughly opposite to the ViewDirection.
        /// </summary>
        public static bool IsElementOnFacingWall(View elevationView, FamilyInstance element)
        {
            try
            {
                if (elevationView.ViewType != ViewType.Elevation) return true;

                Wall hostWall = element.Host as Wall;
                if (hostWall == null) return true; // Not hosted on a wall — include by default

                LocationCurve locCurve = hostWall.Location as LocationCurve;
                if (locCurve == null) return true;

                Line wallLine = locCurve.Curve as Line;
                if (wallLine == null) return true; // Curved wall — include

                // Wall normal: perpendicular to the wall direction in the XY plane
                XYZ wallDir = wallLine.Direction;
                XYZ wallNormal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();

                // Elevation view direction (points from viewer INTO the scene)
                XYZ viewDir = elevationView.ViewDirection;

                // The wall is "facing" the elevation if the wall normal is roughly
                // parallel to the view direction (dot product close to +1 or -1).
                // We use absolute value since wall normal could point either way.
                double dot = Math.Abs(wallNormal.DotProduct(viewDir));

                // Tolerance: cos(30°) ≈ 0.866 — walls within 30° of perpendicular 
                // to the view are considered "facing"
                return dot > 0.7;
            }
            catch
            {
                return true; // On error, include
            }
        }
    }
}
