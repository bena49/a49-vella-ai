// ============================================================================
// A49AIRevitAssistant/Executor/Commands/TagStrategies/TagContext.cs
// ============================================================================
// Shared data classes and helpers used by the Automate Tagging system:
//   - TagRequest: input payload from the wizard
//   - TagResult:  per-view result returned from a strategy
//   - TagHelpers: static helpers for section / elevation visibility checks
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
        /// For SECTION views: determines whether a door or window should be tagged.
        ///
        /// TWO ZONES are checked:
        ///
        ///   ZONE 1 — CUT PLANE (±300mm tolerance):
        ///     The element is directly intersected by the section cut.
        ///     300mm covers a typical wall thickness so elements hosted on
        ///     the cut wall are included on both sides.
        ///     → ALWAYS tag these.
        ///
        ///   ZONE 2 — FAR-CLIP ELEVATION ZONE (beyond cut plane, within far clip):
        ///     The element is visible on the facing wall in the background.
        ///     This happens when the Far Clip Offset is extended far enough to
        ///     show the elevation of the back wall.
        ///     → Tag ONLY if the element's host wall faces the section
        ///       (i.e. the wall is parallel to the section cut plane).
        ///
        ///   SKIP — Anything beyond the far clip, or elements on side walls
        ///           not intersected by the cut.
        /// </summary>
        public static bool IsElementVisibleInSection(View sectionView, FamilyInstance element, XYZ elementPoint)
        {
            try
            {
                if (sectionView.ViewType != ViewType.Section) return true;

                BoundingBoxXYZ cropBox = sectionView.CropBox;
                if (cropBox == null) return true;

                // Transform element world point → view local coordinates.
                // In section view local coords:
                //   X = left/right  |  Y = up/down  |  Z = depth (cut → far clip)
                Transform inv = cropBox.Transform.Inverse;
                XYZ localPoint = inv.OfPoint(elementPoint);

                double cutPlaneZ = cropBox.Min.Z;  // near clip = cut plane
                double farClipZ = cropBox.Max.Z;  // far clip
                double cutTolerance = 300.0 / 304.8; // 300mm in feet

                // ZONE 1: Directly cut by the section plane → always tag
                if (Math.Abs(localPoint.Z - cutPlaneZ) <= cutTolerance)
                    return true;

                // ZONE 2: In the far-clip elevation zone → tag only if on facing wall
                double beyondCut = cutPlaneZ + cutTolerance;
                if (localPoint.Z > beyondCut && localPoint.Z <= farClipZ)
                    return IsElementOnFacingWallOfSection(sectionView, element);

                // Beyond far clip or behind section → skip
                return false;
            }
            catch
            {
                return true; // On error, default to including
            }
        }

        /// <summary>
        /// For ELEVATION views (including interior elevations): checks whether
        /// the element's host wall is the FACING wall of the elevation.
        /// Elements on walls behind or perpendicular to the view are excluded.
        ///
        /// Logic: The elevation's ViewDirection points INTO the view.
        /// A wall "faces" the elevation if its normal is roughly parallel to ViewDirection.
        /// </summary>
        public static bool IsElementOnFacingWall(View elevationView, FamilyInstance element)
        {
            try
            {
                if (elevationView.ViewType != ViewType.Elevation) return true;

                Wall hostWall = element.Host as Wall;
                if (hostWall == null) return true;

                LocationCurve locCurve = hostWall.Location as LocationCurve;
                if (locCurve == null) return true;

                Line wallLine = locCurve.Curve as Line;
                if (wallLine == null) return true; // Curved wall — include

                // Wall normal: perpendicular to wall direction in XY plane
                XYZ wallDir = wallLine.Direction;
                XYZ wallNormal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();

                // Elevation view direction (points from viewer INTO the scene)
                XYZ viewDir = elevationView.ViewDirection;

                // Wall faces the elevation if |dot| > 0.7 (within ~45° of view direction)
                double dot = Math.Abs(wallNormal.DotProduct(viewDir));
                return dot > 0.7;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// For SECTION views Zone 2: checks whether the element's host wall
        /// is the FACING WALL of the section (the back wall being looked at).
        ///
        /// A wall "faces" the section if its normal is roughly parallel to the
        /// section's ViewDirection (same logic as elevation facing wall check).
        /// </summary>
        private static bool IsElementOnFacingWallOfSection(View sectionView, FamilyInstance element)
        {
            try
            {
                Wall hostWall = element.Host as Wall;
                if (hostWall == null) return true;

                LocationCurve locCurve = hostWall.Location as LocationCurve;
                if (locCurve == null) return true;

                Line wallLine = locCurve.Curve as Line;
                if (wallLine == null) return true; // Curved wall — include

                XYZ wallDir = wallLine.Direction;
                XYZ wallNormal = new XYZ(-wallDir.Y, wallDir.X, 0).Normalize();

                XYZ viewDir = sectionView.ViewDirection;

                double dot = Math.Abs(wallNormal.DotProduct(viewDir));
                return dot > 0.7;
            }
            catch
            {
                return true;
            }
        }
    }
}
